// PipelineHandler is a pipeline seam participant exercised through ResourceMethodOperationHandler.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Scheduling.Skeleton;
using Xunit;

namespace Schemata.Report.Tests;

public class ReportMethodHandlerShould
{
    [Fact]
    public async Task Generate_Sync_Persisted_Sets_Snapshot_Operation_To_Terminal_Row() {
        var state      = new ReportPersistenceState();
        var operations = new Mock<IOperationService>();
        Guid? uid      = null;
        string? output = null;
        operations.Setup(service => service.CreateTerminalAsync(
                      "generate", It.IsAny<string?>(), null, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                  .Returns((string method, string? value, string? error, Guid? operationUid, CancellationToken ct) => {
                      uid    = operationUid;
                      output = value;
                      return ValueTask.FromResult(new Operation { Done = true });
                  });

        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            state,
            configure: services => {
                services.AddSingleton(operations.Object);
                services.AddScoped<GenerateHandler<SchemataReport>>();
            });
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<GenerateHandler<SchemataReport>>();

        await handler.InvokeAsync(null, GenerateRequest(persist: true, sync: true), null, null, default);

        var snapshot = Assert.Single(state.Snapshots);
        Assert.NotNull(uid);
        Assert.Equal($"operations/{uid.Value:n}", snapshot.Operation);
        var terminal = JsonSerializer.Deserialize<ReportOperationOutput>(output!, SchemataJson.Default);
        Assert.Equal(snapshot.CanonicalName, terminal!.Snapshot);
        operations.Verify(service => service.CreateTerminalAsync(
            "generate", output, null, uid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Generate_Async_Returns_Pending_Operation() {
        JobContext? context = null;
        var scheduler  = ReportTestHost.CreateScheduler((triggerContext, _) => context = triggerContext);
        var operations = new Mock<IOperationService>(MockBehavior.Strict);
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)),
            configure: services => {
                services.AddSingleton(scheduler.Object);
                services.AddSingleton(operations.Object);
                services.AddScoped<GenerateHandler<SchemataReport>>();
            });
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<GenerateHandler<SchemataReport>>();

        var result = await handler.InvokeAsync(null, GenerateRequest(), null, null, default);

        Assert.False(result.Done);
        Assert.NotNull(context);
        Assert.Equal("generate", context!.Method);
        scheduler.Verify(
            value => value.TriggerAsync<ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>(
                It.IsAny<JobContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        operations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Read_Pages_Through_Chunks_Statelessly() {
        var snapshot = new SchemataReportSnapshot {
            CanonicalName = "reports/daily/snapshots/current",
            ChunkCount    = 3,
        };
        var loadedChunkIndexes = new List<int>();
        var store = CreateSnapshotStore(snapshot, [Chunk(0), Chunk(1), Chunk(2)], loadedChunkIndexes);
        var options = Options.Create(new SchemataReportOptions { MaxReadPageSize = 2_000 });
        var handler = new ReadSnapshotHandler<SchemataReportSnapshot>(store.Object, options);

        var first = await handler.InvokeAsync(
            snapshot.CanonicalName,
            new ReadSnapshotRequest { PageSize = 1_500 },
            snapshot,
            null,
            default);
        var second = await handler.InvokeAsync(
            snapshot.CanonicalName,
            new ReadSnapshotRequest { PageSize = 1_500, PageToken = first.NextPageToken },
            snapshot,
            null,
            default);

        Assert.Equal(1_500, first.Rows.Count);
        Assert.NotNull(first.NextPageToken);
        Assert.Equal(1_500, second.Rows.Count);
        Assert.Null(second.NextPageToken);
        Assert.Equal([0, 1, 1, 2], loadedChunkIndexes);
    }

    [Fact]
    public async Task Read_Invalid_Token_Throws_InvalidArgument() {
        var snapshot = new SchemataReportSnapshot { CanonicalName = "reports/daily/snapshots/current" };
        var options  = Options.Create(new SchemataReportOptions());
        var handler = new ReadSnapshotHandler<SchemataReportSnapshot>(
            CreateSnapshotStore(snapshot, [], []).Object,
            options);

        var error = await Assert.ThrowsAsync<InvalidArgumentException>(async () => {
            await handler.InvokeAsync(
                snapshot.CanonicalName,
                new ReadSnapshotRequest { PageToken = "not-a-report-token" },
                snapshot,
                null,
                default);
        });

        Assert.Contains("not-a-report-token", error.Message);
    }

    [Fact]
    public async Task Read_Clamps_Page_Size_Above_Max() {
        var snapshot = new SchemataReportSnapshot {
            CanonicalName = "reports/daily/snapshots/current",
            ChunkCount    = 3,
        };
        var loadedChunkIndexes = new List<int>();
        var store = CreateSnapshotStore(snapshot, [Chunk(0), Chunk(1), Chunk(2)], loadedChunkIndexes);
        var options = Options.Create(new SchemataReportOptions { MaxReadPageSize = 1_000 });
        var handler = new ReadSnapshotHandler<SchemataReportSnapshot>(store.Object, options);

        var page = await handler.InvokeAsync(
            snapshot.CanonicalName,
            new ReadSnapshotRequest { PageSize = 5_000 },
            snapshot,
            null,
            default);

        Assert.Equal(1_000, page.Rows.Count);
        Assert.NotNull(page.NextPageToken);
        Assert.Equal([0], loadedChunkIndexes);
    }

    [Fact]
    public async Task Generate_With_Name_And_Query_Throws_InvalidArgument() {
        using var services = new ServiceCollection().BuildServiceProvider();
        var handler = new GenerateHandler<SchemataReport>(
            new Mock<IReportService>(MockBehavior.Strict).Object,
            new(),
            services);
        var request = GenerateRequest();
        request.Name = "reports/daily";

        await Assert.ThrowsAsync<InvalidArgumentException>(async () => {
            await handler.InvokeAsync(null, request, null, null, default);
        });
    }

    [Fact]
    public async Task Generate_Without_Operation_Service_Throws_FailedPrecondition() {
        using var services = new ServiceCollection().BuildServiceProvider();
        var handler = new GenerateHandler<SchemataReport>(
            new Mock<IReportService>(MockBehavior.Strict).Object,
            new(),
            services);

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(async () => {
            await handler.InvokeAsync(null, GenerateRequest(), null, null, default);
        });

        Assert.Equal("Report generation requires an operation service.", exception.Message);
    }

    [Fact]
    public async Task Generate_Request_Flows_Through_Collection_Method_Pipeline() {
        using var services = new ServiceCollection().BuildServiceProvider();
        var operation = new ResourceMethodOperationHandler<SchemataReport, GenerateReportRequest, Operation>(
            new Mock<IRepository<SchemataReport>>(MockBehavior.Strict).Object,
            services);
        var handler = new PipelineHandler();
        var request = new GenerateReportRequest { Name = "reports/daily" };

        var response = await operation.InvokeAsync(handler, "generate", null, request, null, default);

        Assert.Same(request, handler.Request);
        Assert.Same(response, handler.Response);
    }

    private static GenerateReportRequest GenerateRequest(bool persist = false, bool sync = false) {
        return new() {
            Persist = persist,
            Sync    = sync,
            Query   = new() { Sources = [new("r", "rows")] },
        };
    }

    private static SchemataReportSnapshotChunk Chunk(int index) {
        var rows = Enumerable.Range(index * 1_000, 1_000)
                             .Select(value => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> {
                                 ["value"] = value,
                             })
                             .ToList();
        return new() {
            Index    = index,
            RowCount = rows.Count,
            Rows     = JsonSerializer.Serialize(rows, SchemataJson.Default),
        };
    }

    private sealed class PipelineHandler : IResourceMethodHandler<SchemataReport, GenerateReportRequest, Operation>
    {
        internal GenerateReportRequest? Request { get; private set; }

        internal Operation Response { get; } = new();

        public ValueTask<Operation> InvokeAsync(
            string?                name,
            GenerateReportRequest  request,
            SchemataReport?        entity,
            System.Security.Claims.ClaimsPrincipal? principal,
            CancellationToken      ct
        ) {
            Request = request;
            return ValueTask.FromResult(Response);
        }
    }

    private static Mock<IReportSnapshotStore> CreateSnapshotStore(
        SchemataReportSnapshot                  snapshot,
        IReadOnlyList<SchemataReportSnapshotChunk> chunks,
        List<int>                               loadedChunkIndexes
    ) {
        var store = new Mock<IReportSnapshotStore>(MockBehavior.Strict);
        store.Setup(value => value.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns((string _, CancellationToken _) => ReportTestRows.ToAsync([snapshot]));
        store.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns((string snapshotName, CancellationToken _) =>
                 ValueTask.FromResult<SchemataReportSnapshot?>(snapshotName == snapshot.CanonicalName ? snapshot : null));
        store.Setup(value => value.GetChunkAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .Returns((string snapshotName, int index, CancellationToken _) => {
                 loadedChunkIndexes.Add(index);
                 return ValueTask.FromResult<SchemataReportSnapshotChunk?>(
                     snapshotName == snapshot.CanonicalName ? chunks.SingleOrDefault(chunk => chunk.Index == index) : null);
             });
        store.Setup(value => value.ReadRowsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns((string _, CancellationToken _) =>
                 ReportTestRows.ToAsync(Array.Empty<IReadOnlyDictionary<string, object?>>()));
        return store;
    }
}
