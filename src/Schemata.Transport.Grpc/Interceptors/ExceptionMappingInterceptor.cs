using System;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Transport.Grpc.Proto;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Transport.Grpc.Interceptors;

/// <summary>
///     Maps <see cref="SchemataException" /> to <see cref="RpcException" /> with
///     <c>google.rpc.Status</c> details (AIP-193). Unhandled exceptions are logged and surface as
///     <see cref="StatusCode.Internal" /> with a non-disclosing message.
/// </summary>
public class ExceptionMappingInterceptor(ILogger<ExceptionMappingInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest                               request,
        ServerCallContext                      context,
        UnaryServerMethod<TRequest, TResponse> continuation
    ) {
        try {
            return await continuation(request, context);
        } catch (RpcException) {
            throw;
        } catch (SchemataException ex) {
            throw BuildRpcException(ex, context);
        } catch (Exception ex) {
            logger.LogError(ex, "Unhandled exception in gRPC call {Method}.", context.Method);
            throw BuildRpcException(
                new(500, ErrorCodes.Internal, SchemataResources.GetResourceString(SchemataResources.GENERIC_ERROR)),
                context
            );
        }
    }

    private static RpcException BuildRpcException(SchemataException ex, ServerCallContext context) {
        var httpContext = context.GetHttpContext();
        var requestId   = httpContext.TraceIdentifier;
        var locale      = ParseAcceptLanguage(httpContext.Request.Headers.AcceptLanguage);

        var rpcStatus = RpcStatusBuilder.Build(ex, requestId, locale);

        var metadata = new Metadata { { "grpc-status-details-bin", rpcStatus.ToByteArray() } };

        return new(new((StatusCode)rpcStatus.Code, ex.Message), metadata);
    }

    /// <summary>
    ///     Extracts the highest-quality language tag from an <c>Accept-Language</c> header
    ///     (e.g. <c>"zh-CN,en-US;q=0.9"</c> -> <c>"zh-CN"</c>). Returns <see langword="null" />
    ///     when the header is empty so the central <c>EnsureLocalizedMessage</c> helper skips
    ///     localization.
    /// </summary>
    private static string? ParseAcceptLanguage(StringValues header) {
        foreach (var value in header) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            foreach (var segment in value.Split(',')) {
                var trimmed = segment.Trim();
                if (trimmed.Length == 0) {
                    continue;
                }

                var semicolon = trimmed.IndexOf(';');
                var tag       = semicolon < 0 ? trimmed : trimmed[..semicolon].Trim();
                if (tag.Length == 0 || tag == "*") {
                    continue;
                }

                return tag;
            }
        }

        return null;
    }
}
