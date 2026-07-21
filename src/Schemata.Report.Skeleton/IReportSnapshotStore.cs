using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Report.Skeleton;

/// <summary>Reads persisted report snapshot headers and their row streams.</summary>
public interface IReportSnapshotStore
{
    /// <summary>Streams metadata headers for a report's snapshots.</summary>
    /// <param name="reportName">The canonical name of the owning report.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The snapshot metadata headers.</returns>
    IAsyncEnumerable<SchemataReportSnapshot> ListAsync(string reportName, CancellationToken ct = default);

    /// <summary>Gets one snapshot's metadata header.</summary>
    /// <param name="snapshotName">The canonical snapshot name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The metadata header, or <see langword="null" /> when it is absent.</returns>
    ValueTask<SchemataReportSnapshot?> GetAsync(string snapshotName, CancellationToken ct = default);

    /// <summary>Gets one persisted row-data chunk, loading only that chunk.</summary>
    /// <param name="snapshotName">The canonical snapshot name.</param>
    /// <param name="index">The zero-based chunk index.</param>
    /// <param name="ct">Cancellation token for the read.</param>
    /// <returns>The requested chunk, or <see langword="null" /> when it is absent.</returns>
    ValueTask<SchemataReportSnapshotChunk?> GetChunkAsync(
        string            snapshotName,
        int               index,
        CancellationToken ct = default);

    /// <summary>Streams snapshot rows in ascending chunk order.</summary>
    /// <remarks>
    ///     Implementations load and decode one persisted chunk at a time. Consumers receive rows in chunk-index
    ///     order, and neither side retains the full snapshot in memory.
    /// </remarks>
    /// <param name="snapshotName">The canonical snapshot name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The streamed snapshot rows.</returns>
    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadRowsAsync(
        string            snapshotName,
        CancellationToken ct = default);
}
