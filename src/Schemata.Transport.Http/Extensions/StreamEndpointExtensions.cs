using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Messaging.Skeleton;

namespace Microsoft.AspNetCore.Builder;

public static class StreamEndpointExtensions
{
    public static RouteHandlerBuilder MapSchemataStream<TRequest, TItem>(this IEndpointRouteBuilder endpoints, string pattern)
        where TRequest : class, IStreamRequest<TItem> {
        return endpoints.MapPost(pattern, (TRequest request, HttpContext context) => {
            var dispatcher = context.RequestServices.GetService<IStreamDispatcher>();
            if (dispatcher is null) {
                context.Response.StatusCode = StatusCodes.Status501NotImplemented;
                return Results.Empty;
            }
            var json = new JsonSerializerOptions(context.RequestServices.GetService<IOptions<JsonSerializerOptions>>()?.Value ?? SchemataJson.Default) {
                WriteIndented = false,
            };
            System.Collections.Generic.IAsyncEnumerable<TItem> sequence;
            try { sequence = dispatcher.Stream<TRequest, TItem>(request, context.User, context.RequestAborted); }
            catch (System.NotSupportedException) {
                context.Response.StatusCode = StatusCodes.Status501NotImplemented;
                return Results.Empty;
            }
            return new NdjsonStreamResult<TItem>(sequence, json);
        });
    }

    private sealed class NdjsonStreamResult<TItem>(
        System.Collections.Generic.IAsyncEnumerable<TItem> sequence,
        JsonSerializerOptions json
    ) : IResult {
        public async Task ExecuteAsync(HttpContext context) {
            var ct = context.RequestAborted;
            System.Collections.Generic.IAsyncEnumerator<TItem>? enumerator = null;
            using var frame = new MemoryStream();
            try {
                enumerator = sequence.GetAsyncEnumerator(ct);
                var hasItem = await enumerator.MoveNextAsync();
                if (hasItem) Serialize(frame, new { item = enumerator.Current });
                context.Response.ContentType = "application/x-ndjson";
                await context.Response.StartAsync(ct);
                while (hasItem) {
                    await Write(context.Response, frame, ct);
                    hasItem = await enumerator.MoveNextAsync();
                    if (hasItem) Serialize(frame, new { item = enumerator.Current });
                }
                await enumerator.DisposeAsync();
                enumerator = null;
                Serialize(frame, new { complete = true });
                await Write(context.Response, frame, ct);
                await context.Response.CompleteAsync();
            } catch (System.OperationCanceledException failure) when (ct.IsCancellationRequested) {
                try {
                    if (enumerator is not null) await enumerator.DisposeAsync();
                } catch (System.Exception cleanup) {
                    throw new System.AggregateException(failure, cleanup);
                } finally {
                    context.Abort();
                }
                throw;
            } catch (System.Exception failure) {
                if (enumerator is not null) {
                    try { await enumerator.DisposeAsync(); }
                    catch (System.Exception cleanup) { failure = new System.AggregateException(failure, cleanup); }
                }
                if (!context.Response.HasStarted) throw;
                var error = failure as SchemataException
                            ?? new SchemataException(500, SchemataConstants.ErrorCodes.Internal, SchemataResources.INTERNAL);
                Serialize(frame, error.CreateErrorResponse(context.TraceIdentifier));
                try { await Write(context.Response, frame, ct); }
                catch { context.Abort(); throw; }
                await context.Response.CompleteAsync();
            }
        }

        private void Serialize<T>(MemoryStream frame, T value) {
            frame.SetLength(0);
            JsonSerializer.Serialize(frame, value, json);
            frame.WriteByte((byte)'\n');
        }

        private async Task Write(HttpResponse response, MemoryStream frame, CancellationToken ct) {
            await response.Body.WriteAsync(frame.GetBuffer().AsMemory(0, checked((int)frame.Length)), ct);
            await response.Body.FlushAsync(ct);
        }
    }
}
