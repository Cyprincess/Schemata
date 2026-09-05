using Schemata.Report.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

using Schemata.Report.Skeleton.Models;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Enums;

namespace Schemata.Report.Tests;

public class ReportGenerationJobShould
{
    [Fact]
    public async Task Generate_Persists_Pending_Row_And_Returns_Addressable_Operation() {
        JobContext?           context   = null;
        SchemataJobExecution? triggered = null;
        var scheduler = ReportTestHost.CreateScheduler((triggerContext, execution) => {
            context   = triggerContext;
            triggered = execution;
        });
        var operations = new Mock<IOperationService>(MockBehavior.Strict);
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => {
                services.AddSingleton(scheduler.Object);
                services.AddSingleton(operations.Object);
            });
        var service = provider.GetRequiredService<IReportService>();

        var operation = await service.GenerateAsync(ReportTestHost.InlineRequest());

        Assert.NotNull(triggered);
        Assert.NotNull(context);
        var uid = Assert.IsType<Guid>(context.ExecutionUid);
        Assert.Equal(uid, triggered!.Uid);
        Assert.Equal($"operations/{triggered.Uid:n}", operation.CanonicalName);
        Assert.False(operation.Done);
        Assert.Equal("generate", context.Method);
        scheduler.Verify(
            value => value.TriggerAsync<ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>(
                It.IsAny<JobContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Job_Replays_Request_From_ArgsJson() {
        var runHandler = new Mock<IRequestHandler<RunReportRequest, ReportResult>>(MockBehavior.Strict);
        runHandler.Setup(value => value.HandleAsync(
                             It.Is<RunReportRequest>(replayed =>
                                 replayed.Request.Persist
                              && replayed.Request.Query != null
                              && replayed.Principal == null),
                             It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new ReportResult());
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => ReplaceRunHandler(services, runHandler.Object));
        var job = CreateJob(provider);
        var request = ReportTestHost.InlineRequest(persist: true);

        await job.ExecuteAsync(new() {
            ArgsJson = JsonSerializer.Serialize(request, SchemataJson.Default),
        }, CancellationToken.None);

        runHandler.VerifyAll();
    }

    [Fact]
    public async Task Job_Writes_Snapshot_Ref_To_Output() {
        var expected = new ReportResult { Snapshot = "reports/daily/snapshots/s1" };
        var runHandler = new Mock<IRequestHandler<RunReportRequest, ReportResult>>(MockBehavior.Strict);
        runHandler.Setup(value => value.HandleAsync(
                             It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(expected);
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => ReplaceRunHandler(services, runHandler.Object));
        var job = CreateJob(provider);
        var execution = new SchemataJobExecution { Uid = Guid.NewGuid() };

        await job.ExecuteAsync(new() {
            ArgsJson  = JsonSerializer.Serialize(ReportTestHost.InlineRequest(persist: true), SchemataJson.Default),
            Execution = execution,
        }, CancellationToken.None);

        Assert.NotNull(execution.Output);
        var output = JsonSerializer.Deserialize<ReportOperationOutput>(execution.Output!, SchemataJson.Default);
        Assert.NotNull(output);
        Assert.Equal("reports/daily/snapshots/s1", output.Snapshot);
        Assert.Null(output.Response);
    }

    [Fact]
    public async Task Ephemeral_Output_Respects_MaxInlineRows() {
        var response = new ReportResult();
        response.Response.Rows.Add(new Dictionary<string, object?>());
        response.Response.Rows.Add(new Dictionary<string, object?>());
        response.Response.Rows.Add(new Dictionary<string, object?>());
        var runHandler = new Mock<IRequestHandler<RunReportRequest, ReportResult>>(MockBehavior.Strict);
        runHandler.Setup(value => value.HandleAsync(
                             It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(response);
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            maxInlineRows: 2,
            configure: services => ReplaceRunHandler(services, runHandler.Object));
        var job = CreateJob(provider);

        var exception = await Assert.ThrowsAsync<ReportException>(async () => {
            await job.ExecuteAsync(new() {
                ArgsJson  = JsonSerializer.Serialize(ReportTestHost.InlineRequest(), SchemataJson.Default),
                Execution = new(),
            }, CancellationToken.None);
        });

        Assert.Contains("Persist=true", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Job_Stops_At_Chunk_Boundary_On_Cancelled() {
        var uid = Guid.NewGuid();
        var state = new ReportPersistenceState {
            CancelAfterChunks = 2,
            Execution = new() {
                Uid   = uid,
                State = ExecutionState.Running,
            },
        };
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(8)),
            state,
            chunkSize: 2,
            configure: services => services.AddScoped<IRepository<SchemataJobExecution>>(_ => state.CreateExecutionRepository()));
        var job = CreateJob(provider);

        await job.ExecuteAsync(new() {
            ExecutionUid = uid,
            ArgsJson     = JsonSerializer.Serialize(ReportTestHost.InlineRequest(persist: true), SchemataJson.Default),
            Execution    = state.Execution,
        }, CancellationToken.None);

        Assert.Equal(ExecutionState.Cancelled, state.Execution!.State);
        Assert.Equal(SnapshotState.Cancelled, Assert.Single(state.Snapshots).State);
        Assert.Equal(2, state.Chunks.Count);
        Assert.Equal(0, state.ExecutionCommitCount);

        var cancelledSnapshot = state.Snapshots[0];
        Assert.Equal(
            new[] { SnapshotState.Pending, SnapshotState.Running, SnapshotState.Cancelled },
            state.SnapshotStateSequence);
        Assert.Equal(2, state.ChunkAddSequence.Count);
        for (var index = 0; index < state.ChunkAddSequence.Count; index++) {
            var chunk = state.ChunkAddSequence[index];
            Assert.Equal($"chunk-{index}", chunk.Name);
            Assert.Equal(index, chunk.Index);
            Assert.Equal($"{cancelledSnapshot.CanonicalName}/chunks/chunk-{index}", chunk.CanonicalName);
            Assert.Equal(cancelledSnapshot.Report, chunk.Report);
            Assert.Equal(cancelledSnapshot.Name, chunk.Snapshot);
            Assert.Equal(2, chunk.RowCount);
        }
        Assert.Equal(2, state.ChunkRepositoryInstances);
    }

    [Fact]
    public void KeyResolver_Resolves_Closed_Generic() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseReport());
        using var app = builder.Build();
        var resolver = app.Services
                          .GetServices<IScheduledJobKeyResolver>()
                          .OfType<ReportJobKeyResolver<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>()
                          .Single();

        var type = resolver.ResolveType(ReportJobKeyResolver.Key);

        Assert.Equal(typeof(ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>), type);
        Assert.Equal(ReportJobKeyResolver.Key, resolver.ResolveKey(type!));
    }

    [Fact]
    public async Task Job_Malformed_ArgsJson_Throws_Without_Mutating_Execution() {
        using var provider = ReportTestHost.Create(ReportTestHost.CreateDriver(ReportTestRows.Create(1)));
        var job = CreateJob(provider);
        var execution = new SchemataJobExecution {
            State = ExecutionState.Running,
        };

        await Assert.ThrowsAsync<JsonException>(async () => {
            await job.ExecuteAsync(new() {
                ArgsJson  = "{bad",
                Execution = execution,
            }, CancellationToken.None);
        });

        Assert.Equal(ExecutionState.Running, execution.State);
        Assert.Null(execution.Output);
    }

    [Fact]
    public async Task Job_From_ArgsJson_Marks_Snapshot_ImmediatePersisted() {
        var state = new ReportPersistenceState();
        using var provider = ReportTestHost.Create(ReportTestHost.CreateDriver(ReportTestRows.Create(1)), state);
        var job = CreateJob(provider);

        await job.ExecuteAsync(new() {
            ArgsJson  = JsonSerializer.Serialize(ReportTestHost.InlineRequest(persist: true), SchemataJson.Default),
            Execution = new(),
        }, CancellationToken.None);

        Assert.Equal(ReportRunKind.ImmediatePersisted, Assert.Single(state.Snapshots).RunKind);
    }

    [Fact]
    public async Task Job_From_Variable_Fire_Marks_Snapshot_Scheduled() {
        var state  = new ReportPersistenceState();
        var report = new SchemataReport { Name = "daily", CanonicalName = "reports/daily" };
        using var provider = ReportTestHost.Create(ReportTestHost.CreateDriver(ReportTestRows.Create(1)), state, report: report);
        var job = CreateJob(provider);

        await job.ExecuteAsync(new() {
            Variables = new Dictionary<string, string?> { ["report"] = "daily" },
            Execution = new(),
        }, CancellationToken.None);

        Assert.Equal(ReportRunKind.Scheduled, Assert.Single(state.Snapshots).RunKind);
    }

    private static void ReplaceRunHandler(
        IServiceCollection                                            services,
        IRequestHandler<RunReportRequest, ReportResult> handler
    ) {
        services.RemoveAll<IRequestHandler<RunReportRequest, ReportResult>>();
        services.RemoveAll<IReportService>();
        services.AddSingleton(new Mock<IReportService>(MockBehavior.Strict).Object);
        services.AddSingleton(handler);
    }

    private static ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk> CreateJob(
        ServiceProvider provider
    ) {
        return new(provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<IOptions<SchemataReportOptions>>());
    }
}
