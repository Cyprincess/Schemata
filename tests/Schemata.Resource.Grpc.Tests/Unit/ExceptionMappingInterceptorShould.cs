using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Grpc.Interceptors;
using Xunit;

namespace Schemata.Resource.Grpc.Tests.Unit;

public class ExceptionMappingInterceptorShould
{
    private readonly ExceptionMappingInterceptor _interceptor = new();

    private static Exception CreateException(Type t) {
        if (t == typeof(NotFoundException)) return new NotFoundException();
        if (t == typeof(AuthorizationException)) return new AuthorizationException();
        if (t == typeof(ConcurrencyException)) return new ConcurrencyException();
        if (t == typeof(InvalidArgumentException)) return new InvalidArgumentException();
        if (t == typeof(ValidationException)) return new ValidationException(new List<ErrorFieldViolation>());
        throw new ArgumentOutOfRangeException(nameof(t), t, "Unrecognised exception type");
    }

    [Theory]
    [InlineData(typeof(NotFoundException), StatusCode.NotFound)]
    [InlineData(typeof(AuthorizationException), StatusCode.PermissionDenied)]
    [InlineData(typeof(ConcurrencyException), StatusCode.Aborted)]
    [InlineData(typeof(ValidationException), StatusCode.InvalidArgument)]
    [InlineData(typeof(InvalidArgumentException), StatusCode.InvalidArgument)]
    public async Task UnaryServerHandler_SchemataException_MapsToExpectedStatusCode(
        Type       exceptionType,
        StatusCode expected
    ) {
        var ctx = new TestServerCallContext();
        var ex  = CreateException(exceptionType);

        var rpc = await Assert.ThrowsAsync<RpcException>(() => _interceptor.UnaryServerHandler<object, object>(new(), ctx, (_, _) => throw ex));

        Assert.Equal(expected, rpc.Status.StatusCode);
    }

    [Fact]
    public async Task UnaryServerHandler_UnhandledException_MapsToInternal() {
        var ctx = new TestServerCallContext();

        var rpc = await Assert.ThrowsAsync<RpcException>(() => _interceptor.UnaryServerHandler<object, object>(
                                                             new(), ctx, (_, _) => throw new("unexpected")));

        Assert.Equal(StatusCode.Internal, rpc.Status.StatusCode);
    }

    [Fact]
    public async Task UnaryServerHandler_RpcException_PassesThrough() {
        var ctx      = new TestServerCallContext();
        var original = new RpcException(new(StatusCode.Cancelled, "cancelled"));

        var rpc = await Assert.ThrowsAsync<RpcException>(() => _interceptor.UnaryServerHandler<object, object>(
                                                             new(), ctx, (_, _) => throw original));

        Assert.Equal(StatusCode.Cancelled, rpc.Status.StatusCode);
    }
}
