using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Transport.Grpc.Interceptors;
using Xunit;
using Status = Google.Rpc.Status;
using static Schemata.Abstractions.SchemataConstants;

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

    [Fact]
    public async Task Maps_Validation_Exception_Preserving_Field_Reason_And_Localized_Message() {
        var logger      = new Mock<ILogger<ExceptionMappingInterceptor>>();
        var interceptor = new ExceptionMappingInterceptor(logger.Object);
        var exception   = new ValidationException([
            new ErrorFieldViolation {
                Field            = "name",
                Reason           = "REQUIRED",
                Description      = "Name is required",
                LocalizedMessage = new() { Locale = "en-US", Message = "Please enter your name." },
            },
            new ErrorFieldViolation { Field = "age", Description = "Age must be between 1 and 150." },
        ]);

        var thrown = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler<object, object>(
            new(),
            Context(),
            (_, _) => throw exception));

        Assert.Equal(StatusCode.InvalidArgument, thrown.StatusCode);

        var status = Status.Parser.ParseFrom(thrown.Trailers.GetValueBytes("grpc-status-details-bin"));

        var errorInfo = status.Details
                             .Single(d => d.TypeUrl.EndsWith("google.rpc.ErrorInfo", StringComparison.Ordinal))
                             .Unpack<ErrorInfo>();
        Assert.Equal(ErrorReasons.ValidationFailed, errorInfo.Reason);

        var badRequest = status.Details
                              .Single(d => d.TypeUrl.EndsWith("google.rpc.BadRequest", StringComparison.Ordinal))
                              .Unpack<BadRequest>();
        var violations = badRequest.FieldViolations;

        Assert.Equal(2, violations.Count);
        Assert.Equal("name", violations[0].Field);
        Assert.Equal("Name is required", violations[0].Description);
        Assert.Equal("REQUIRED", violations[0].Reason);
        Assert.Equal("en-US", violations[0].LocalizedMessage!.Locale);
        Assert.Equal("Please enter your name.", violations[0].LocalizedMessage!.Message);
        Assert.Equal("age", violations[1].Field);
        Assert.Equal("", violations[1].Reason);
        Assert.Null(violations[1].LocalizedMessage);
    }

    private static ServerCallContext Context() {
        var context = TestServerCallContext.Create(
            "method",
            "host",
            DateTime.MaxValue,
            new(),
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
