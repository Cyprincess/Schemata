using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using static Schemata.Abstractions.SchemataConstants;
using Xunit;

namespace Schemata.Report.Tests;

public class ReportEntryEquivalenceShould
{
    [Fact]
    public async Task Run_Facade_Continues_Command_Context_Into_Report_Advisors() {
        var commandAdvisor = new RecordingRunCommandAdvisor();
        var reportAdvisor  = new RecordingReportAdvisor();
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => {
                services.AddSingleton<IRequestPipelineAdvisor<RunReportRequest, ReportResult>>(commandAdvisor);
                services.AddSingleton<IReportGenerateAdvisor>(reportAdvisor);
            });
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        var result = await provider.GetRequiredService<IReportService>()
                                   .RunAsync(ReportTestHost.InlineRequest(), principal);

        Assert.Single(result.Response.Rows);
        Assert.Same(principal, commandAdvisor.Principal);
        Assert.True(reportAdvisor.SawMarker);
    }

    [Fact]
    public async Task Run_Facade_Continues_Command_Context_Into_The_Snapshot_Advisor() {
        var commandAdvisor  = new RecordingRunCommandAdvisor();
        var snapshotAdvisor = new RecordingSnapshotAdvisor();
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => {
                services.AddSingleton<IRequestPipelineAdvisor<RunReportRequest, ReportResult>>(commandAdvisor);
                services.AddSingleton<IReportSnapshotAdvisor>(snapshotAdvisor);
            });
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        await provider.GetRequiredService<IReportService>()
                      .RunAsync(ReportTestHost.InlineRequest(persist: true), principal);

        Assert.True(snapshotAdvisor.Ran);
        Assert.True(snapshotAdvisor.SawMarker);
    }

    [Fact]
    public async Task Generate_Facade_Dispatches_Wire_Command_To_Scheduler() {
        GenerateReportRequest? dispatched = null;
        var commandAdvisor = new Mock<IRequestPipelineAdvisor<GenerateReportRequest, Operation>>();
        commandAdvisor.SetupGet(value => value.Order).Returns(0);
        commandAdvisor.Setup(value => value.AdviseAsync(
                                  It.IsAny<AdviceContext>(),
                                  It.IsAny<GenerateReportRequest>(),
                                  It.IsAny<RequestHandlerContinuation<Operation>>(),
                                  It.IsAny<CancellationToken>()))
                      .Returns((AdviceContext _, GenerateReportRequest request, RequestHandlerContinuation<Operation> next, CancellationToken ct) => {
                          dispatched = request;
                          return next(ct);
                      });
        JobContext? staged = null;
        var scheduler = ReportTestHost.CreateScheduler((context, _) => staged = context);
        var operations = new Mock<IOperationService>(MockBehavior.Strict);
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => {
                services.AddSingleton(commandAdvisor.Object);
                services.AddSingleton(scheduler.Object);
                services.AddSingleton(operations.Object);
            });
        var request = ReportTestHost.InlineRequest(persist: true);

        var operation = await provider.GetRequiredService<IReportService>().GenerateAsync(request);

        Assert.NotNull(dispatched);
        Assert.Same(request.Query, dispatched.Query);
        Assert.True(dispatched.Persist);
        Assert.False(dispatched.Sync);
        Assert.Null(dispatched.Principal);
        Assert.NotNull(staged);
        Assert.Equal(Verbs.Generate, staged.Method);
        Assert.Equal($"operations/{staged.ExecutionUid!.Value:n}", operation.CanonicalName);
    }

    [Fact]
    public void Run_And_Generate_Handlers_Are_Keyed_Unkeyed_And_Commands_Round_Trip() {
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)));

        AssertHandler<RunReportRequest, ReportResult,
            RunReportHandler<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>(provider);
        AssertHandler<GenerateReportRequest, Operation,
            GenerateHandler<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>(provider);

        var principal = new ClaimsPrincipal(new ClaimsIdentity("serialized"));
        var runJson = JsonSerializer.Serialize(
            new RunReportRequest(ReportTestHost.InlineRequest(persist: true), principal),
            SchemataJson.Default);
        Assert.DoesNotContain("principal", runJson, StringComparison.OrdinalIgnoreCase);
        var run = Assert.IsType<RunReportRequest>(
            JsonSerializer.Deserialize<RunReportRequest>(runJson, SchemataJson.Default));
        Assert.True(run.Request.Persist);
        Assert.NotNull(run.Request.Query);
        Assert.Null(run.Principal);

        var generateJson = JsonSerializer.Serialize(new GenerateReportRequest {
            Name      = "reports/daily",
            Persist   = true,
            Sync      = true,
            Principal = principal,
        }, SchemataJson.Default);
        Assert.DoesNotContain("principal", generateJson, StringComparison.OrdinalIgnoreCase);
        var generate = Assert.IsType<GenerateReportRequest>(
            JsonSerializer.Deserialize<GenerateReportRequest>(generateJson, SchemataJson.Default));
        Assert.Equal("reports/daily", generate.Name);
        Assert.True(generate.Persist);
        Assert.True(generate.Sync);
        Assert.Null(generate.Principal);
    }

    private static void AssertHandler<TRequest, TResponse, THandler>(IServiceProvider provider)
        where TRequest : IRequest<TResponse>
        where THandler : IRequestHandler<TRequest, TResponse> {
        Assert.IsType<THandler>(provider.GetRequiredService<IRequestHandler<TRequest, TResponse>>());
        Assert.IsType<THandler>(provider.GetRequiredKeyedService<IRequestHandler<TRequest, TResponse>>(
            ReportConstants.Handlers.Default));
    }

    private sealed record Marker;

    private sealed class RecordingRunCommandAdvisor : IRequestPipelineAdvisor<RunReportRequest, ReportResult>
    {
        public int Order => 0;

        public ClaimsPrincipal? Principal { get; private set; }

        public Task<ReportResult> AdviseAsync(
            AdviceContext                           ctx,
            RunReportRequest                        request,
            RequestHandlerContinuation<ReportResult> next,
            CancellationToken                       ct = default) {
            Principal = request.Principal;
            ctx.Set(new Marker());
            return next(ct);
        }
    }

    private sealed class RecordingReportAdvisor : IReportGenerateAdvisor
    {
        public int Order => 0;

        public bool SawMarker { get; private set; }

        public Task<AdviseResult> AdviseAsync(
            AdviceContext         ctx,
            ReportGenerateContext context,
            CancellationToken     ct = default
        ) {
            SawMarker = ctx.TryGet<Marker>(out _);
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class RecordingSnapshotAdvisor : IReportSnapshotAdvisor
    {
        public int Order => 0;

        public bool Ran { get; private set; }

        public bool SawMarker { get; private set; }

        public Task<AdviseResult> AdviseAsync(
            AdviceContext         ctx,
            ReportSnapshotContext context,
            CancellationToken     ct = default
        ) {
            Ran       = true;
            SawMarker = ctx.TryGet<Marker>(out _);
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
