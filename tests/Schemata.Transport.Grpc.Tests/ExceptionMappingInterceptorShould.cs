using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Transport.Grpc.Interceptors;
using Xunit;

namespace Schemata.Transport.Grpc.Tests;

public sealed class ExceptionMappingInterceptorShould
{
    [Fact]
    public async Task Logs_Error_And_Maps_Unhandled_Exception_To_Internal_Status() {
        var logger      = new Mock<ILogger<ExceptionMappingInterceptor>>();
        var interceptor = new ExceptionMappingInterceptor(logger.Object);
        var failure     = new InvalidOperationException("boom");

        var thrown = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler<object, object>(
            new(),
            Context(),
            (_, _) => throw failure));

        Assert.Equal(StatusCode.Internal, thrown.StatusCode);
        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                failure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Maps_Schemata_Exception_Without_Logging() {
        var logger      = new Mock<ILogger<ExceptionMappingInterceptor>>();
        var interceptor = new ExceptionMappingInterceptor(logger.Object);

        var thrown = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler<object, object>(
            new(),
            Context(),
            (_, _) => throw new NotFoundException()));

        Assert.Equal(StatusCode.NotFound, thrown.StatusCode);
        logger.VerifyNoOtherCalls();
    }

    private static ServerCallContext Context() {
        var context = TestServerCallContext.Create(
            "method",
            "host",
            DateTime.MaxValue,
            new Metadata(),
            CancellationToken.None,
            "peer",
            null,
            null,
            _ => Task.CompletedTask,
            () => new(),
            _ => { });
        context.UserState["__HttpContext"] = new DefaultHttpContext();
        return context;
    }
}
