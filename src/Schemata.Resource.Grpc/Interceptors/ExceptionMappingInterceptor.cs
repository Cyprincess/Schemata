using System;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Grpc.Proto;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Grpc.Interceptors;

/// <summary>
///     gRPC interceptor that maps <see cref="SchemataException" /> instances to
///     <see cref="RpcException" /> with Google RPC status details
///     per <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Unhandled exceptions produce an INTERNAL status with a generic message.
/// </summary>
public class ExceptionMappingInterceptor : Interceptor
{
    /// <inheritdoc />
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest                               request,
        ServerCallContext                      context,
        UnaryServerMethod<TRequest, TResponse> continuation
    ) {
        try {
            return await continuation(request, context);
        } catch (RpcException) {
            // Intended RpcExceptions propagate as-is — only SchemataExceptions
            // and unhandled errors need mapping.
            throw;
        } catch (SchemataException ex) {
            throw BuildRpcException(ex, context);
        } catch (Exception) {
            // AIP-193: unhandled server errors map to Internal with a non-disclosing message.
            throw BuildRpcException(new(500, ErrorCodes.Internal, SchemataResources.GetResourceString(SchemataResources.ST1012)), context);
        }
    }

    private static RpcException BuildRpcException(SchemataException ex, ServerCallContext context) {
        var httpContext = context.GetHttpContext();
        var requestId   = httpContext.TraceIdentifier;

        // Build structured google.rpc.Status including error details and request
        // identifier, then attach as grpc-status-details-bin metadata.
        var rpcStatus = RpcStatusBuilder.Build(ex, requestId);

        var metadata = new Metadata { { "grpc-status-details-bin", rpcStatus.ToByteArray() } };

        return new(new((StatusCode)rpcStatus.Code, ex.Message), metadata);
    }
}
