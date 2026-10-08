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
            new() {
                Field            = "name",
                Reason           = "REQUIRED",
                Description      = "Name is required",
                LocalizedMessage = new() { Locale = "en-US", Message = "Please enter your name." },
            },
            new() { Field = "age", Description = "Age must be between 1 and 150." },
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

    [Fact]
    public async Task Maps_Quota_Exception_Preserving_The_Complete_Violation_Payload() {
        var logger      = new Mock<ILogger<ExceptionMappingInterceptor>>();
        var interceptor = new ExceptionMappingInterceptor(logger.Object);
        var exception   = new QuotaExceededException([
            new() {
                Subject          = "project:my-project",
                Description      = "Quota 'CPUS-PER-VM-FAMILY' exceeded",
                ApiService       = "compute.googleapis.com",
                QuotaMetric      = "compute.googleapis.com/cpus_per_vm_family",
                QuotaId          = "CPUS-PER-VM-FAMILY-per-project-region",
                QuotaDimensions  = new() { ["region"] = "us-central1", ["vm_family"] = "n1" },
                QuotaValue       = 24,
                FutureQuotaValue = 48,
            },
            new() { Subject = "client:192.168.1.1", Description = "Requests per minute exceeded" },
        ]);

        var thrown = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler<object, object>(
            new(),
            Context(),
            (_, _) => throw exception));

        Assert.Equal(StatusCode.ResourceExhausted, thrown.StatusCode);

        var status = Status.Parser.ParseFrom(thrown.Trailers.GetValueBytes("grpc-status-details-bin"));

        var quota = status.Details
                          .Single(d => d.TypeUrl.EndsWith("google.rpc.QuotaFailure", StringComparison.Ordinal))
                          .Unpack<QuotaFailure>();

        Assert.Equal(2, quota.Violations.Count);

        var first = quota.Violations[0];
        Assert.Equal("project:my-project", first.Subject);
        Assert.Equal("Quota 'CPUS-PER-VM-FAMILY' exceeded", first.Description);
        Assert.Equal("compute.googleapis.com", first.ApiService);
        Assert.Equal("compute.googleapis.com/cpus_per_vm_family", first.QuotaMetric);
        Assert.Equal("CPUS-PER-VM-FAMILY-per-project-region", first.QuotaId);
        Assert.Equal(2, first.QuotaDimensions.Count);
        Assert.Equal("us-central1", first.QuotaDimensions["region"]);
        Assert.Equal("n1", first.QuotaDimensions["vm_family"]);
        Assert.Equal(24, first.QuotaValue);
        Assert.True(first.HasFutureQuotaValue);
        Assert.Equal(48, first.FutureQuotaValue);

        var second = quota.Violations[1];
        Assert.Equal("client:192.168.1.1", second.Subject);
        Assert.Equal("", second.ApiService);
        Assert.Equal("", second.QuotaMetric);
        Assert.Equal("", second.QuotaId);
        Assert.Empty(second.QuotaDimensions);
        Assert.Equal(0, second.QuotaValue);
        Assert.False(second.HasFutureQuotaValue);
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
