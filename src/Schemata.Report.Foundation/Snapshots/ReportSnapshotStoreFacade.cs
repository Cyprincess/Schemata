using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation.Snapshots;

/// <summary>
///     The framework-owned <see cref="IReportSnapshotStore" /> every host resolves. It rejects use
///     while conflicting Report entity triples are registered — before any repository I/O — then
///     delegates to the explicitly selected custom implementation (builder
///     <c>UseSnapshotStore&lt;TStore&gt;</c>) or the Foundation default. Applications replacing the
///     public interface directly own its complete contract.
/// </summary>
/// <typeparam name="TSnapshot">Persisted report-snapshot entity type.</typeparam>
public sealed class ReportSnapshotStoreFacade<TSnapshot>(
    ReportRegistration registration,
    IServiceProvider   services
) : IReportSnapshotStore
    where TSnapshot : SchemataReportSnapshot, new()
{
    private readonly ReportRegistration _registration = registration;
    private readonly IServiceProvider   _services     = services;

    private IReportSnapshotStore Selected() {
        return _services.GetKeyedService<IReportSnapshotStore>(ReportConstants.Services.Selected)
            ?? _services.GetRequiredKeyedService<IReportSnapshotStore>(ReportConstants.Services.Default);
    }

    #region IReportSnapshotStore Members

    public async IAsyncEnumerable<SchemataReportSnapshot> ListAsync(
        string                                  reportName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default
    ) {
        _registration.EnsureSingleTriple<TSnapshot>();
        await foreach (var snapshot in Selected().ListAsync(reportName, ct)) {
            yield return snapshot;
        }
    }

    public ValueTask<SchemataReportSnapshot?> GetAsync(string snapshotName, CancellationToken ct = default) {
        _registration.EnsureSingleTriple<TSnapshot>();
        return Selected().GetAsync(snapshotName, ct);
    }

    public ValueTask<SchemataReportSnapshotChunk?> GetChunkAsync(
        string            snapshotName,
        int               index,
        CancellationToken ct = default
    ) {
        _registration.EnsureSingleTriple<TSnapshot>();
        return Selected().GetChunkAsync(snapshotName, index, ct);
    }

    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadRowsAsync(
        string                                  snapshotName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default
    ) {
        _registration.EnsureSingleTriple<TSnapshot>();
        await foreach (var row in Selected().ReadRowsAsync(snapshotName, ct)) {
            yield return row;
        }
    }

    #endregion
}
