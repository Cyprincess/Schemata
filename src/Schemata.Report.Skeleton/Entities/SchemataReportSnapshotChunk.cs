using System;
using System.ComponentModel;
using Schemata.Abstractions.Entities;

namespace Schemata.Report.Skeleton;

/// <summary>Internal persisted row-data chunk belonging to a report snapshot.</summary>
[CanonicalName("reports/{report}/snapshots/{snapshot}/chunks/{chunk}")]
[DisplayName("Chunk")]
public class SchemataReportSnapshotChunk : IIdentifier, ICanonicalName
{
    /// <summary>Leaf identifier of the report that owns the containing snapshot.</summary>
    public string? Report { get; set; }

    /// <summary>Leaf identifier of the snapshot that owns this chunk.</summary>
    public string? Snapshot { get; set; }

    /// <summary>Zero-based position of this chunk within its snapshot.</summary>
    public int Index { get; set; }

    /// <summary>Number of rows encoded in <see cref="Rows" />.</summary>
    public int RowCount { get; set; }

    /// <summary>JSON array containing the chunk's report rows.</summary>
    public string? Rows { get; set; }

    #region ICanonicalName Members

    /// <summary>Leaf identifier used to construct the chunk canonical name.</summary>
    public string? Name { get; set; }

    /// <summary>Fully-qualified canonical name of this chunk.</summary>
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    /// <summary>Stable persistence identifier for this chunk.</summary>
    public Guid Uid { get; set; }

    #endregion
}
