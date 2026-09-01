using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;
using Xunit;

namespace Schemata.Report.Tests;

public class ReportRetentionShould
{
    private static readonly DateTime Anchor = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Retention_MaxCount_Removes_Oldest_With_Chunks() {
        var clock = new MutableClock(Anchor);
        var state = new ReportPersistenceState();
        state.SuccessfulCaptureTimes.Enqueue(Anchor.AddMinutes(-3));
        state.SuccessfulCaptureTimes.Enqueue(Anchor.AddMinutes(-2));
        state.SuccessfulCaptureTimes.Enqueue(Anchor.AddMinutes(-1));
        var report = Report("daily", new() { MaxCount = 2 });
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)), state, chunkSize: 1, report: report,
            configure: services => services.AddSingleton<TimeProvider>(clock));
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
        var clock = new MutableClock(Anchor);
        var state = new ReportPersistenceState();
        var expired = Snapshot("daily", "expired", SnapshotState.Succeeded, Anchor.AddDays(-2));
        state.Snapshots.Add(expired);
        state.Chunks.Add(Chunk(expired));
        var report = Report("daily", new() { MaxAgeDays = 1 });
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)), state, report: report,
            configure: services => services.AddSingleton<TimeProvider>(clock));
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.NamedRequest("daily"));

        Assert.DoesNotContain(state.Snapshots, snapshot => snapshot.Name == expired.Name);
        Assert.DoesNotContain(state.Chunks, chunk => chunk.Snapshot == expired.Name);
    }

    [Fact]
    public async Task Retention_MaxAge_Cutoff_Is_Exact_To_A_Tick() {
        var clock  = new MutableClock(Anchor);
        var state  = new ReportPersistenceState();
        var cutoff = Anchor.AddDays(-1);
        var stale  = Snapshot("daily", "stale", SnapshotState.Succeeded, cutoff.AddTicks(-1));
        var fresh  = Snapshot("daily", "fresh", SnapshotState.Succeeded, cutoff.AddTicks(1));
        state.Snapshots.AddRange([stale, fresh]);
        state.Chunks.AddRange([Chunk(stale), Chunk(fresh)]);
        var report = Report("daily", new() { MaxAgeDays = 1 });
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)), state, report: report,
            configure: services => services.AddSingleton<TimeProvider>(clock));
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.NamedRequest("daily"));

        Assert.DoesNotContain(state.Snapshots, snapshot => snapshot.Name == stale.Name);
        Assert.Contains(state.Snapshots, snapshot => snapshot.Name == fresh.Name);
    }

    [Fact]
    public async Task Failed_Snapshot_Chunks_Reclaimed() {
        var clock = new MutableClock(Anchor);
        var state = new ReportPersistenceState();
        var failed = Snapshot("daily", "failed", SnapshotState.Failed, Anchor.AddDays(-2));
        var cancelled = Snapshot("daily", "cancelled", SnapshotState.Cancelled, Anchor.AddDays(-2));
        state.Snapshots.AddRange([failed, cancelled]);
        state.Chunks.AddRange([Chunk(failed), Chunk(cancelled)]);
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)), state, report: Report("daily", new()),
            configure: services => services.AddSingleton<TimeProvider>(clock));
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.NamedRequest("daily"));

        Assert.DoesNotContain(state.Snapshots, snapshot => snapshot.Name == failed.Name || snapshot.Name == cancelled.Name);
        Assert.DoesNotContain(state.Chunks, chunk => chunk.Snapshot == failed.Name || chunk.Snapshot == cancelled.Name);
    }

    [Fact]
    public async Task No_Retention_Config_Keeps_All() {
        var state = new ReportPersistenceState();
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)), state, report: Report("daily", null));
        var service = provider.GetRequiredService<IReportService>();

        await service.RunAsync(ReportTestHost.NamedRequest("daily"));
        await service.RunAsync(ReportTestHost.NamedRequest("daily"));
        await service.RunAsync(ReportTestHost.NamedRequest("daily"));

        Assert.Equal(3, state.Snapshots.Count);
        Assert.Equal(3, state.Chunks.Count);
    }

    [Fact]
    public async Task EnforceAsync_Selects_MaxCount_MaxAge_And_Incomplete_Victims_Only() {
        var clock = new MutableClock(Anchor);
        var state = new ReportPersistenceState();

        var daily  = "daily";
        var weekly = "weekly";
        var newestSucceeded    = Snapshot(daily, "newest", SnapshotState.Succeeded, Anchor);
        var middleSucceeded    = Snapshot(daily, "middle", SnapshotState.Succeeded, Anchor.AddMinutes(-5));
        var oldestSucceeded    = Snapshot(daily, "oldest", SnapshotState.Succeeded, Anchor.AddDays(-3));
        var countOnlySucceeded = Snapshot(daily, "count-only", SnapshotState.Succeeded, Anchor);
        countOnlySucceeded.CapturedAt = null;
        countOnlySucceeded.UpdateTime = null;
        countOnlySucceeded.CreateTime = null;
        var oldFailed          = Snapshot(daily, "old-failed", SnapshotState.Failed, Anchor.AddDays(-2));
        var freshFailed        = Snapshot(daily, "fresh-failed", SnapshotState.Failed, Anchor.AddMinutes(-30));
        var oldCancelled       = Snapshot(daily, "old-cancelled", SnapshotState.Cancelled, Anchor.AddDays(-2).AddMinutes(-30));
        var oldPending         = Snapshot(daily, "old-pending", SnapshotState.Pending, Anchor.AddDays(-5));
        var weeklyOldSucceeded = Snapshot(weekly, "weekly-old", SnapshotState.Succeeded, Anchor.AddDays(-10));
        newestSucceeded.Uid          = Guid.Parse("11111111-0000-0000-0000-000000000001");
        middleSucceeded.Uid          = Guid.Parse("11111111-0000-0000-0000-000000000002");
        oldestSucceeded.Uid          = Guid.Parse("11111111-0000-0000-0000-000000000003");
        countOnlySucceeded.Uid       = Guid.Parse("11111111-0000-0000-0000-000000000004");
        oldFailed.Uid                = Guid.Parse("11111111-0000-0000-0000-000000000005");
        oldFailed.UpdateTime         = Anchor.AddDays(-2);
        freshFailed.Uid              = Guid.Parse("11111111-0000-0000-0000-000000000006");
        freshFailed.UpdateTime       = Anchor.AddMinutes(-30);
        oldCancelled.Uid             = Guid.Parse("11111111-0000-0000-0000-000000000007");
        oldCancelled.UpdateTime      = Anchor.AddDays(-2).AddMinutes(-30);
        oldPending.Uid               = Guid.Parse("11111111-0000-0000-0000-000000000008");
        oldPending.UpdateTime        = Anchor.AddDays(-5);
        weeklyOldSucceeded.Uid       = Guid.Parse("22222222-0000-0000-0000-000000000001");
        foreach (var snapshot in new[] {
                     newestSucceeded, middleSucceeded, oldestSucceeded, countOnlySucceeded,
                     oldFailed, freshFailed, oldCancelled, oldPending, weeklyOldSucceeded,
                 }) {
            snapshot.CanonicalName = $"reports/{snapshot.Report}/snapshots/{snapshot.Name}";
        }

        state.Snapshots.AddRange([
            newestSucceeded, middleSucceeded, oldestSucceeded, countOnlySucceeded,
            oldFailed, freshFailed, oldCancelled, oldPending, weeklyOldSucceeded,
        ]);

        foreach (var snapshot in state.Snapshots) {
            var chunk = new SchemataReportSnapshotChunk {
                Report   = snapshot.Report,
                Snapshot = snapshot.Name,
                Name     = "chunk-0",
                Index    = 0,
            };
            chunk.Uid          = Guid.NewGuid();
            chunk.CanonicalName = $"reports/{snapshot.Report}/snapshots/{snapshot.Name}/chunks/chunk-0";
            state.Chunks.Add(chunk);
        }
        var report = Report(daily, new() { MaxCount = 3, MaxAgeDays = 2 });
        using var provider = ReportTestHost.Create(
            ReportTestHost.CreateDriver(ReportTestRows.Create(1)), state, report: report,
            configure: services => services.AddSingleton<TimeProvider>(clock));
        var enforcer = provider.GetRequiredService<ReportRetentionEnforcer<SchemataReportSnapshot, SchemataReportSnapshotChunk>>();

        await enforcer.EnforceAsync(report);

        var snapshotNames = state.Snapshots.Select(value => value.Name!).ToHashSet(StringComparer.Ordinal);
        var chunkSnapshots = state.Chunks.Select(value => value.Snapshot!).ToHashSet(StringComparer.Ordinal);
        var chunkCanonicalNames = state.Chunks.Select(value => value.CanonicalName!).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(newestSucceeded.Name!, snapshotNames);
        Assert.Contains(middleSucceeded.Name!, snapshotNames);
        Assert.Contains(freshFailed.Name!, snapshotNames);
        Assert.Contains(oldPending.Name!, snapshotNames);
        Assert.Contains(weeklyOldSucceeded.Name!, snapshotNames);

        Assert.DoesNotContain(oldestSucceeded.Name!, snapshotNames);
        Assert.DoesNotContain(countOnlySucceeded.Name!, snapshotNames);
        Assert.DoesNotContain(oldFailed.Name!, snapshotNames);
        Assert.DoesNotContain(oldCancelled.Name!, snapshotNames);

        Assert.Equal(5, state.Snapshots.Count);

        foreach (var retained in new[] { newestSucceeded, middleSucceeded, freshFailed, oldPending, weeklyOldSucceeded }) {
            Assert.Contains(retained.Name!, chunkSnapshots);
        }

        foreach (var removed in new[] { oldestSucceeded, countOnlySucceeded, oldFailed, oldCancelled }) {
            Assert.DoesNotContain(removed.Name!, chunkSnapshots);
            Assert.DoesNotContain(
                $"reports/{daily}/snapshots/{removed.Name}/chunks/chunk-0",
                chunkCanonicalNames);
        }

        Assert.All(state.Chunks, chunk => {
            Assert.Equal("chunk-0", chunk.Name);
            Assert.Equal(0, chunk.Index);
            Assert.EndsWith("/chunks/chunk-0", chunk.CanonicalName ?? string.Empty);
        });
        Assert.Equal(5, state.Chunks.Count);
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

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() { return _now; }

        public void Advance(TimeSpan delta) { _now += delta; }
    }
}
