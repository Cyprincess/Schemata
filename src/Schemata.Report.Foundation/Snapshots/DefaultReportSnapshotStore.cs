using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation.Snapshots;

/// <summary>Reads report snapshot headers and decodes one persisted chunk at a time.</summary>
/// <typeparam name="TSnapshot">Persisted snapshot-header entity type.</typeparam>
/// <typeparam name="TChunk">Persisted snapshot-chunk entity type.</typeparam>
public sealed class DefaultReportSnapshotStore<TSnapshot, TChunk>(IServiceScopeFactory scopes) : IReportSnapshotStore
    where TSnapshot : SchemataReportSnapshot
    where TChunk : SchemataReportSnapshotChunk
{
    public async IAsyncEnumerable<SchemataReportSnapshot> ListAsync(
        string reportName,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var parsed = ResourceNameDescriptor.ForType<SchemataReport>().ParseCanonicalName(reportName);
        if (parsed is not { } target || string.IsNullOrWhiteSpace(target.LeafName) || target.LeafName == "-") {
            throw new InvalidArgumentException(SchemataResources.INVALID_NAME, new Dictionary<string, string?> { ["name"] = reportName });
        }

        var report = target.LeafName;
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TSnapshot>>();
        await foreach (var snapshot in repository.ListAsync(query => query.Where(candidate => candidate.Report == report), ct)) {
            yield return snapshot;
        }
    }

    public async ValueTask<SchemataReportSnapshot?> GetAsync(string snapshotName, CancellationToken ct = default) {
        var (report, snapshot) = ParseSnapshotName(snapshotName);
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TSnapshot>>();
        return await repository.FirstOrDefaultAsync(
                   query => query.Where(candidate => candidate.CanonicalName == snapshotName
                                                  && candidate.Report == report && candidate.Name == snapshot), ct);
    }

    public async ValueTask<SchemataReportSnapshotChunk?> GetChunkAsync(
        string            snapshotName,
        int               index,
        CancellationToken ct = default
    ) {
        var (report, snapshot) = ParseSnapshotName(snapshotName);
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TChunk>>();
        return await repository.FirstOrDefaultAsync(
                   query => query.Where(candidate => candidate.Report == report
                                                  && candidate.Snapshot == snapshot && candidate.Index == index), ct);
    }

    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadRowsAsync(
        string snapshotName,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var (report, snapshot) = ParseSnapshotName(snapshotName);
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TChunk>>();
        await foreach (var chunk in repository.ListAsync(
                           query => query.Where(candidate => candidate.Report == report && candidate.Snapshot == snapshot)
                                         .OrderBy(candidate => candidate.Index), ct)) {
            var rows = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(chunk.Rows ?? "[]", SchemataJson.Default)
                       ?? [];
            foreach (var row in rows) {
                ct.ThrowIfCancellationRequested();
                yield return row;
            }
        }
    }

    private static (string Report, string Snapshot) ParseSnapshotName(string name) {
        var parsed = ResourceNameDescriptor.ForType<SchemataReportSnapshot>().ParseCanonicalName(name);
        if (parsed is not { } target
            || string.IsNullOrWhiteSpace(target.LeafName) || target.LeafName == "-"
            || !target.ParentValues.TryGetValue("report", out var report)
            || string.IsNullOrWhiteSpace(report) || report == "-") {
            throw new InvalidArgumentException(SchemataResources.INVALID_NAME, new Dictionary<string, string?> { ["name"] = name });
        }

        return (report, target.LeafName);
    }

}
