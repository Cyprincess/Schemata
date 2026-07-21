using System;
using System.ComponentModel;
using Schemata.Abstractions.Entities;
using Schemata.Insight.Skeleton;

namespace Schemata.Report.Skeleton;

/// <summary>Metadata header for a persisted report snapshot whose rows are stored in chunks.</summary>
[CanonicalName("reports/{report}/snapshots/{snapshot}")]
[DisplayName("Snapshot")]
public class SchemataReportSnapshot : IIdentifier, ICanonicalName, ITimestamp
{
    /// <summary>Leaf identifier of the parent report.</summary>
    public string? Report { get; set; }

    /// <summary>Identifies whether generation was immediate or scheduled.</summary>
    public ReportRunKind RunKind { get; set; }

    /// <summary>Lifecycle state of this materialization.</summary>
    public SnapshotState State { get; set; }

    /// <summary>Canonical name of the operation that generated this snapshot, when applicable.</summary>
    public string? Operation { get; set; }

    /// <summary>Time at which the generated data was captured.</summary>
    public DateTime? CapturedAt { get; set; }

    /// <summary>Total rows written after materialization completes.</summary>
    public int? RowCount { get; set; }

    /// <summary>Total chunks written after materialization completes.</summary>
    public int? ChunkCount { get; set; }

    /// <summary>JSON-serialized <see cref="FieldDescriptor" /> array describing the row shape.</summary>
    public string? Schema { get; set; }

    /// <summary>Error text recorded when materialization fails.</summary>
    public string? Error { get; set; }

    #region ICanonicalName Members

    /// <summary>Leaf identifier used to construct the snapshot canonical name.</summary>
    public string? Name { get; set; }

    /// <summary>Fully-qualified canonical name of this snapshot.</summary>
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    /// <summary>Stable persistence identifier for this snapshot.</summary>
    public Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    /// <summary>Time at which this snapshot header was created.</summary>
    public DateTime? CreateTime { get; set; }

    /// <summary>Time at which this snapshot header was last updated.</summary>
    public DateTime? UpdateTime { get; set; }

    #endregion
}
