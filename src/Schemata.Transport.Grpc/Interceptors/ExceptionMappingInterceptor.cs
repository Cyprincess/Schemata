using System;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Globalization;
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
        var locale      = AcceptLanguageParser.Parse(httpContext.Request.Headers.AcceptLanguage)?.Name;

        var rpcStatus = RpcStatusBuilder.Build(ex, requestId, locale);

        var metadata = new Metadata { { "grpc-status-details-bin", rpcStatus.ToByteArray() } };

        return new(new((StatusCode)rpcStatus.Code, ex.Message), metadata);
    }
}
