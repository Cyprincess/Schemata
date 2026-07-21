using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Reads report snapshot headers and decodes one persisted chunk at a time.</summary>
/// <typeparam name="TSnapshot">Persisted snapshot-header entity type.</typeparam>
/// <typeparam name="TChunk">Persisted snapshot-chunk entity type.</typeparam>
public sealed class DefaultReportSnapshotStore<TSnapshot, TChunk>(IServiceScopeFactory scopes) : IReportSnapshotStore
    where TSnapshot : SchemataReportSnapshot
    where TChunk : SchemataReportSnapshotChunk
{
    /// <inheritdoc />
    public async IAsyncEnumerable<SchemataReportSnapshot> ListAsync(
        string reportName,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var report = Leaf(reportName);
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TSnapshot>>();
        await foreach (var snapshot in repository.ListAsync(query => query.Where(candidate => candidate.Report == report), ct)) {
            yield return snapshot;
        }
    }

    /// <inheritdoc />
    public async ValueTask<SchemataReportSnapshot?> GetAsync(string snapshotName, CancellationToken ct = default) {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TSnapshot>>();
        return await repository.FirstOrDefaultAsync(query => query.Where(candidate => candidate.CanonicalName == snapshotName), ct);
    }

    /// <inheritdoc />
    public async ValueTask<SchemataReportSnapshotChunk?> GetChunkAsync(
        string            snapshotName,
        int               index,
        CancellationToken ct = default
    ) {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TChunk>>();
        var snapshot   = Leaf(snapshotName);
        return await repository.FirstOrDefaultAsync(
                   query => query.Where(candidate => candidate.Snapshot == snapshot && candidate.Index == index), ct);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadRowsAsync(
        string snapshotName,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TChunk>>();
        var snapshot = Leaf(snapshotName);
        await foreach (var chunk in repository.ListAsync(
                           query => query.Where(candidate => candidate.Snapshot == snapshot)
                                         .OrderBy(candidate => candidate.Index), ct)) {
            var rows = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(chunk.Rows ?? "[]", SchemataJson.Default)
                       ?? [];
            foreach (var row in rows) {
                ct.ThrowIfCancellationRequested();
                yield return row;
            }
        }
    }

    private static string Leaf(string canonicalName) {
        var index = canonicalName.LastIndexOf('/');
        return index < 0 ? canonicalName : canonicalName[(index + 1)..];
    }
}
