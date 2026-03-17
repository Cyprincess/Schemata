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

public class ExceptionMappingInterceptor : Interceptor
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
        } catch (Exception) {
            throw BuildRpcException(new(500, ErrorCodes.Internal, SchemataResources.GetResourceString(SchemataResources.ST1018)), context);
        }
    }

    private static RpcException BuildRpcException(SchemataException ex, ServerCallContext context) {
        var httpContext = context.GetHttpContext();
        var requestId   = httpContext.TraceIdentifier;

        var rpcStatus = RpcStatusBuilder.Build(ex, requestId);

        var metadata = new Metadata { { "grpc-status-details-bin", rpcStatus.ToByteArray() } };

        return new(new((StatusCode)rpcStatus.Code, ex.Message), metadata);
    }
}
