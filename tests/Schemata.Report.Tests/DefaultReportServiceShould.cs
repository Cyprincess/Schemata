using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Skeleton;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;
using Xunit;

namespace Schemata.Report.Tests;

public class DefaultReportServiceShould
{
    [Fact]
    public async Task Run_Inline_Returns_Rows_Under_Cap() {
        var probe = new ReportMaterializerProbe(ReportTestRows.Create(3));
        using var provider = ReportTestHost.Create(probe, maxInlineRows: 3);
        var service = provider.GetRequiredService<IReportService>();

        var result = await service.RunAsync(ReportTestHost.InlineRequest());

        Assert.Null(result.Snapshot);
        Assert.Equal(3, result.Response.Rows.Count);
        Assert.Equal(3, result.Response.TotalSize);
    }

    [Fact]
    public async Task Run_Inline_Beyond_MaxInlineRows_Throws_With_Persist_Hint() {
        var probe = new ReportMaterializerProbe(ReportTestRows.Create(3));
        using var provider = ReportTestHost.Create(probe, maxInlineRows: 2);
        var service = provider.GetRequiredService<IReportService>();

        var exception = await Assert.ThrowsAsync<ReportException>(async () => {
            await service.RunAsync(ReportTestHost.InlineRequest());
        });

        Assert.Contains("Persist=true", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_Persist_Writes_Header_And_Chunks_With_Fresh_Scope_Per_Chunk() {
        var state = new ReportPersistenceState();
        var probe = new ReportMaterializerProbe(ReportTestRows.Create(5));
        using var provider = ReportTestHost.Create(probe, state, chunkSize: 2);
        var service = provider.GetRequiredService<IReportService>();

        var result = await service.RunAsync(ReportTestHost.InlineRequest(persist: true));

        var snapshot = Assert.Single(state.Snapshots);
        Assert.Equal(SnapshotState.Succeeded, snapshot.State);
        Assert.Equal(5, snapshot.RowCount);
        Assert.Equal(3, snapshot.ChunkCount);
        Assert.Equal(3, state.Chunks.Count);
        Assert.Equal(result.Snapshot, snapshot.CanonicalName);
        Assert.Equal(3, state.ChunkRepositoryInstances);
    }

    [Fact]
    public async Task Failed_Materialization_Marks_Header_Failed_And_Keeps_Chunks() {
        var state = new ReportPersistenceState();
        var probe = new ReportMaterializerProbe(ReportTestRows.ThrowAfter(2, "source failed"));
        using var provider = ReportTestHost.Create(probe, state, chunkSize: 2);
        var service = provider.GetRequiredService<IReportService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await service.RunAsync(ReportTestHost.InlineRequest(persist: true));
        });

        var snapshot = Assert.Single(state.Snapshots);
        Assert.Equal("source failed", exception.Message);
        Assert.Equal(SnapshotState.Failed, snapshot.State);
        Assert.Equal("source failed", snapshot.Error);
        Assert.Single(state.Chunks);
    }

    [Fact]
    public async Task Run_Propagates_Generate_Advisor_Rejection() {
        var probe = new ReportMaterializerProbe(ReportTestRows.Create(1));
        using var provider = ReportTestHost.Create(
            probe,
            configure: services => services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IReportGenerateAdvisor>(new RejectingAdvisor())));
        var service = provider.GetRequiredService<IReportService>();

        await Assert.ThrowsAsync<PermissionDeniedException>(async () => {
            await service.RunAsync(ReportTestHost.InlineRequest());
        });
    }

    [Fact]
    public async Task Run_Forwards_Caller_Principal_To_Materializer() {
        var probe     = new ReportMaterializerProbe(ReportTestRows.Create(1));
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        using var provider = ReportTestHost.Create(probe);
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.InlineRequest(), principal);

        Assert.Same(principal, probe.Principal);
    }

    [Fact]
    public async Task Generate_Advisor_May_Replace_The_Principal() {
        var probe       = new ReportMaterializerProbe(ReportTestRows.Create(1));
        var substituted = new ClaimsPrincipal(new ClaimsIdentity("service"));
        using var provider = ReportTestHost.Create(
            probe,
            configure: services => services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IReportGenerateAdvisor>(new SubstitutingAdvisor(substituted))));
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.InlineRequest(), new ClaimsPrincipal(new ClaimsIdentity("caller")));

        Assert.Same(substituted, probe.Principal);
    }

    [Fact]
    public async Task ReadRows_Streams_Chunks_In_Index_Order() {
        var state = new ReportPersistenceState();
        var probe = new ReportMaterializerProbe(ReportTestRows.Create(5));
        using var provider = ReportTestHost.Create(probe, state, chunkSize: 2);
        var service = provider.GetRequiredService<IReportService>();
        var store = provider.GetRequiredService<IReportSnapshotStore>();

        var result = await service.RunAsync(ReportTestHost.InlineRequest(persist: true));
        var values = new List<int>();
        await foreach (var row in store.ReadRowsAsync(result.Snapshot!)) {
            values.Add(((System.Text.Json.JsonElement)row["value"]!).GetInt32());
        }

        Assert.Equal([0, 1, 2, 3, 4], values);
    }

    [Fact]
    public async Task Generate_Without_Scheduler_Throws_FailedPrecondition() {
        using var provider = ReportTestHost.Create(new(ReportTestRows.Create(1)));
        var service = provider.GetRequiredService<IReportService>();

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(async () => {
            await service.GenerateAsync(ReportTestHost.InlineRequest());
        });

        Assert.Equal("Report generation requires a scheduler.", exception.Message);
    }

    [Fact]
    public async Task Run_With_Name_And_Query_Throws_InvalidArgument() {
        using var provider = ReportTestHost.Create(new(ReportTestRows.Create(1)));
        var service = provider.GetRequiredService<IReportService>();
        var request = ReportTestHost.InlineRequest();
        request.Name = "reports/daily";

        await Assert.ThrowsAsync<InvalidArgumentException>(async () => {
            await service.RunAsync(request);
        });
    }

    [Fact]
    public async Task Run_Unknown_Named_Report_Throws_NotFound() {
        using var provider = ReportTestHost.Create(new(ReportTestRows.Create(1)));
        var service = provider.GetRequiredService<IReportService>();

        var exception = await Assert.ThrowsAsync<NotFoundException>(async () => {
            await service.RunAsync(ReportTestHost.NamedRequest("daily"));
        });

        Assert.Equal("Report 'daily' was not found.", exception.Message);
    }

    private sealed class RejectingAdvisor : IReportGenerateAdvisor
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext         ctx,
            ReportGenerateContext context,
            CancellationToken     ct = default
        ) {
            throw new PermissionDeniedException();
        }
    }

    private sealed class SubstitutingAdvisor(ClaimsPrincipal principal) : IReportGenerateAdvisor
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext         ctx,
            ReportGenerateContext context,
            CancellationToken     ct = default
        ) {
            context.Principal = principal;
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
