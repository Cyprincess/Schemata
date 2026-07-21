using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Report.Skeleton;
using Xunit;

namespace Schemata.Report.Tests;

public class ReportRetentionShould
{
    [Fact]
    public async Task Retention_MaxCount_Removes_Oldest_With_Chunks() {
        var now = DateTime.UtcNow;
        var state = new ReportPersistenceState();
        state.SuccessfulCaptureTimes.Enqueue(now.AddMinutes(-3));
        state.SuccessfulCaptureTimes.Enqueue(now.AddMinutes(-2));
        state.SuccessfulCaptureTimes.Enqueue(now.AddMinutes(-1));
        var report = Report("daily", new() { MaxCount = 2 });
        using var provider = ReportTestHost.Create(
            new ReportMaterializerProbe(ReportTestRows.Create(1)), state, chunkSize: 1, report: report);
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.NamedRequest("daily"));
        var oldest = Assert.Single(state.Snapshots);
        await service.RunAsync(ReportTestHost.NamedRequest("daily"));
        await service.RunAsync(ReportTestHost.NamedRequest("daily"));

        Assert.Equal(2, state.Snapshots.Count);
        Assert.DoesNotContain(state.Snapshots, snapshot => snapshot.Name == oldest.Name);
        Assert.DoesNotContain(state.Chunks, chunk => chunk.Snapshot == oldest.Name);
        Assert.All(state.Chunks, chunk => Assert.Contains(state.Snapshots, snapshot => snapshot.Name == chunk.Snapshot));
    }

    [Fact]
    public async Task Retention_MaxAge_Removes_Expired() {
        var state = new ReportPersistenceState();
        var expired = Snapshot("daily", "expired", SnapshotState.Succeeded, DateTime.UtcNow.AddDays(-2));
        state.Snapshots.Add(expired);
        state.Chunks.Add(Chunk(expired));
        var report = Report("daily", new() { MaxAgeDays = 1 });
        using var provider = ReportTestHost.Create(
            new ReportMaterializerProbe(ReportTestRows.Create(1)), state, report: report);
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.NamedRequest("daily"));

        Assert.DoesNotContain(state.Snapshots, snapshot => snapshot.Name == expired.Name);
        Assert.DoesNotContain(state.Chunks, chunk => chunk.Snapshot == expired.Name);
    }

    [Fact]
    public async Task Failed_Snapshot_Chunks_Reclaimed() {
        var state = new ReportPersistenceState();
        var failed = Snapshot("daily", "failed", SnapshotState.Failed, DateTime.UtcNow.AddDays(-2));
        var cancelled = Snapshot("daily", "cancelled", SnapshotState.Cancelled, DateTime.UtcNow.AddDays(-2));
        state.Snapshots.AddRange([failed, cancelled]);
        state.Chunks.AddRange([Chunk(failed), Chunk(cancelled)]);
        using var provider = ReportTestHost.Create(
            new ReportMaterializerProbe(ReportTestRows.Create(1)), state, report: Report("daily", new()));
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.NamedRequest("daily"));

        Assert.DoesNotContain(state.Snapshots, snapshot => snapshot.Name == failed.Name || snapshot.Name == cancelled.Name);
        Assert.DoesNotContain(state.Chunks, chunk => chunk.Snapshot == failed.Name || chunk.Snapshot == cancelled.Name);
    }

    [Fact]
    public async Task No_Retention_Config_Keeps_All() {
        var state = new ReportPersistenceState();
        using var provider = ReportTestHost.Create(
            new ReportMaterializerProbe(ReportTestRows.Create(1)), state, report: Report("daily", null));
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.NamedRequest("daily"));
        await service.RunAsync(ReportTestHost.NamedRequest("daily"));
        await service.RunAsync(ReportTestHost.NamedRequest("daily"));

        Assert.Equal(3, state.Snapshots.Count);
        Assert.Equal(3, state.Chunks.Count);
    }

    private static SchemataReport Report(string name, ReportRetention? retention) {
        return new() {
            Name      = name,
            Retention = retention,
        };
    }

    private static SchemataReportSnapshot Snapshot(string report, string name, SnapshotState state, DateTime capturedAt) {
        return new() {
            Name       = name,
            Report     = report,
            State      = state,
            CapturedAt = capturedAt,
        };
    }

    private static SchemataReportSnapshotChunk Chunk(SchemataReportSnapshot snapshot) {
        return new() {
            Report   = snapshot.Report,
            Snapshot = snapshot.Name,
            Name     = "chunk-0",
        };
    }
}
