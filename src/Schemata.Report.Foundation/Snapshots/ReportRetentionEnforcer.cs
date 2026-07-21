using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Entity.Repository;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Reclaims expired persisted snapshots and their chunks for a retained report definition.</summary>
/// <typeparam name="TSnapshot">Persisted snapshot-header entity type.</typeparam>
/// <typeparam name="TChunk">Persisted snapshot-chunk entity type.</typeparam>
/// <remarks>
///     Runs on the write path after a successful snapshot. Each victim's header and chunks are removed in one
///     unit of work, with cancellation checked between victims.
/// </remarks>
public sealed class ReportRetentionEnforcer<TSnapshot, TChunk>
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    private readonly TimeSpan             _incompleteGracePeriod;
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Creates a write-path retention enforcer.</summary>
    /// <param name="scopes">Factory creating isolated cleanup scopes.</param>
    /// <param name="options">Retention cleanup settings.</param>
    public ReportRetentionEnforcer(
        IServiceScopeFactory            scopes,
        IOptions<SchemataReportOptions> options
    ) {
        _scopes                 = scopes;
        _incompleteGracePeriod  = options.Value.IncompleteSnapshotGracePeriod;
    }

    /// <summary>Applies the parent report's retention policy to its persisted snapshots.</summary>
    /// <param name="report">Parent definition of the newly persisted snapshot.</param>
    /// <param name="ct">Cancellation token observed before each victim cleanup starts.</param>
    public async ValueTask EnforceAsync(SchemataReport? report, CancellationToken ct = default) {
        if (report?.Retention is null || string.IsNullOrWhiteSpace(report.Name)) {
            return;
        }

        var snapshots = await ListAsync(report.Name, ct);
        var victims   = SelectVictims(snapshots, report.Retention, DateTime.UtcNow);
        foreach (var victim in victims) {
            ct.ThrowIfCancellationRequested();
            await RemoveAsync(victim);
        }
    }

    private async ValueTask<List<TSnapshot>> ListAsync(string report, CancellationToken ct) {
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TSnapshot>>();
        var snapshots = new List<TSnapshot>();
        await foreach (var snapshot in repository.ListAsync(query => query.Where(item => item.Report == report), ct)) {
            snapshots.Add(snapshot);
        }

        return snapshots;
    }

    private IReadOnlyList<TSnapshot> SelectVictims(
        IReadOnlyList<TSnapshot> snapshots,
        ReportRetention          retention,
        DateTime                 now
    ) {
        var victims = new List<TSnapshot>();
        var succeeded = snapshots.Where(snapshot => snapshot.State is SnapshotState.Succeeded)
                                .OrderByDescending(SnapshotTime)
                                .ThenByDescending(snapshot => snapshot.Uid)
                                .ToArray();

        if (retention.MaxCount is { } maxCount) {
            Add(victims, succeeded.Skip(Math.Max(maxCount, 0)));
        }

        if (retention.MaxAgeDays is { } maxAgeDays) {
            Add(victims, succeeded.Where(snapshot => IsOlderThan(snapshot, now.AddDays(-maxAgeDays))));
        }

        var incompleteCutoff = now - _incompleteGracePeriod;
        Add(victims, snapshots.Where(snapshot => snapshot.State is SnapshotState.Failed or SnapshotState.Cancelled)
                               .Where(snapshot => IsOlderThan(snapshot, incompleteCutoff)));

        return victims.OrderBy(SnapshotTime).ThenBy(snapshot => snapshot.Uid).ToArray();
    }

    private async Task RemoveAsync(TSnapshot victim) {
        await using var scope = _scopes.CreateAsyncScope();
        var snapshots = scope.ServiceProvider.GetRequiredService<IRepository<TSnapshot>>();
        var chunks    = scope.ServiceProvider.GetRequiredService<IRepository<TChunk>>();
        await using var unit = snapshots.Begin();
        chunks.Join(unit);
        try {
            var snapshot = await snapshots.FirstOrDefaultAsync(query => query.Where(item => item.Uid == victim.Uid), CancellationToken.None);
            if (snapshot is null) {
                await unit.RollbackAsync(CancellationToken.None);
                return;
            }

            var related = new List<TChunk>();
            await foreach (var chunk in chunks.ListAsync(
                               query => query.Where(item => item.Report == snapshot.Report && item.Snapshot == snapshot.Name),
                               CancellationToken.None)) {
                related.Add(chunk);
            }

            await chunks.RemoveRangeAsync(related, CancellationToken.None);
            await snapshots.RemoveRangeAsync([snapshot], CancellationToken.None);
            await unit.CommitAsync(CancellationToken.None);
        } catch {
            await unit.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void Add(List<TSnapshot> victims, IEnumerable<TSnapshot> candidates) {
        foreach (var candidate in candidates) {
            if (!victims.Contains(candidate)) {
                victims.Add(candidate);
            }
        }
    }

    private static bool IsOlderThan(TSnapshot snapshot, DateTime cutoff) {
        return SnapshotTime(snapshot) is { } capturedAt && capturedAt < cutoff;
    }

    private static DateTime? SnapshotTime(TSnapshot snapshot) {
        return snapshot.CapturedAt ?? snapshot.UpdateTime ?? snapshot.CreateTime;
    }
}
