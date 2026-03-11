using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Schemata.Abstractions.Exceptions;

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
            throw MapToRpcException(ex);
        } catch (Exception ex) {
            throw new RpcException(new(StatusCode.Internal, ex.Message));
        }
    }

    private static RpcException MapToRpcException(SchemataException ex) {
        var statusCode = MapStatusCode(ex);

        if (ex is NoContentException) {
            return new(new(StatusCode.OK, string.Empty));
        }

        var message = ex.Message ?? string.Empty;

        if (ex.Errors is { Count: > 0 }) {
            var metadata = new Metadata();
            foreach (var (key, value) in ex.Errors) {
                metadata.Add($"error-{key}", value);
            }

            return new(new(statusCode, message), metadata);
        }

        if (!string.IsNullOrWhiteSpace(ex.Error)) {
            var metadata = new Metadata { { "error", ex.Error } };
            return new(new(statusCode, message), metadata);
        }

        return new(new(statusCode, message));
    }

    private static StatusCode MapStatusCode(SchemataException ex) {
        return ex.Code switch {
            "PERMISSION_DENIED" => StatusCode.PermissionDenied,
            "INVALID_ARGUMENT"  => StatusCode.InvalidArgument,
            "ABORTED"           => StatusCode.Aborted,
            "NOT_FOUND"         => StatusCode.NotFound,
            "OK"                => StatusCode.OK,
            var _               => MapFromHttpStatus(ex.StatusCode),
        };
    }

    private static StatusCode MapFromHttpStatus(int httpStatus) {
        return httpStatus switch {
            400   => StatusCode.InvalidArgument,
            401   => StatusCode.Unauthenticated,
            403   => StatusCode.PermissionDenied,
            404   => StatusCode.NotFound,
            409   => StatusCode.Aborted,
            412   => StatusCode.FailedPrecondition,
            422   => StatusCode.InvalidArgument,
            429   => StatusCode.ResourceExhausted,
            499   => StatusCode.Cancelled,
            500   => StatusCode.Internal,
            501   => StatusCode.Unimplemented,
            503   => StatusCode.Unavailable,
            504   => StatusCode.DeadlineExceeded,
            var _ => StatusCode.Internal,
        };
    }
}
