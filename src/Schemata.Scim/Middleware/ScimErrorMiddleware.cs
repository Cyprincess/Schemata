using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Scim.Middleware;

internal sealed class ScimErrorMiddleware(
    RequestDelegate next,
    ILogger<ScimErrorMiddleware> logger,
    IOptions<ScimServiceProviderOptions> options
)
{
    public async Task InvokeAsync(HttpContext context) {
        if (!ScimResults.IsScimPath(context.Request.Path, options.Value.BasePath)) {
            await next(context);
            return;
        }

        if (HasBoundedBody(context.Request)) {
            if (context.Request.ContentLength > options.Value.MaxRequestBodySize) {
                await ScimResults.WriteErrorAsync(context.Response, StatusCodes.Status413PayloadTooLarge,
                                                  detail: "The SCIM request body exceeds the configured size limit.");
                return;
            }

            context.Request.Body = new LimitedReadStream(context.Request.Body, options.Value.MaxRequestBodySize);
        }

        try {
            await next(context);
        } catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
            throw;
        } catch (ScimProtocolException error) when (!context.Response.HasStarted) {
            logger.LogWarning("SCIM request rejected with status {Status}.", error.Status);
            await ScimResults.WriteErrorAsync(context.Response, error.Status, error.ScimType, error.Message);
            return;
        } catch (Exception error) {
            if (context.Response.HasStarted) {
                throw;
            }

            var status = error is SchemataException { Code: >= 400 and <= 599 } failure ? failure.Code : 500;
            var detail = error is SchemataException and { Message.Length: > 0 } ? error.Message : "An internal error occurred.";

            logger.LogError(error, "SCIM request failed with status {Status}.", status);
            await ScimResults.WriteErrorAsync(context.Response, status, detail: detail);
            return;
        }

        if (context.Response.HasStarted) {
            return;
        }

        switch (context.Response.StatusCode) {
            case StatusCodes.Status404NotFound:
                await ScimResults.WriteErrorAsync(context.Response, StatusCodes.Status404NotFound, detail: "The SCIM endpoint was not found.");
                break;
            case StatusCodes.Status405MethodNotAllowed:
                await ScimResults.WriteErrorAsync(context.Response, StatusCodes.Status405MethodNotAllowed, detail: "The HTTP method is not supported for this SCIM endpoint.");
                break;
            case StatusCodes.Status415UnsupportedMediaType:
                await ScimResults.WriteErrorAsync(context.Response, StatusCodes.Status415UnsupportedMediaType,
                                                  ScimConstants.InvalidSyntax, ScimConstants.UnsupportedMediaTypeDetail);
                break;
        }
    }

    private static bool HasBoundedBody(HttpRequest request) {
        return request.Method is not ("GET" or "HEAD" or "DELETE" or "OPTIONS");
    }

    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) {
            var read = inner.Read(buffer, offset, count);
            Count(read);
            return read;
        }

        public override int Read(Span<byte> buffer) {
            var read = inner.Read(buffer);
            Count(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            Count(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            Count(read);
            return read;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void Count(int count) {
            _read += count;
            if (_read > limit) {
                throw new ScimProtocolException(StatusCodes.Status413PayloadTooLarge, null, "The SCIM request body exceeds the configured size limit.");
            }
        }
    }
}
