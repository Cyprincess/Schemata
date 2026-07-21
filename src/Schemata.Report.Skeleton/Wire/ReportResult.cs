using Schemata.Insight.Skeleton;

namespace Schemata.Report.Skeleton;

/// <summary>Inline report response and the optional persisted snapshot reference.</summary>
public sealed class ReportResult
{
    /// <summary>Inline query result containing rows, schema, and pagination metadata.</summary>
    public QueryInsightResponse Response { get; set; } = new();

    /// <summary>Canonical name of the persisted snapshot when <see cref="ReportRequest.Persist" /> is enabled.</summary>
    public string? Snapshot { get; set; }
}
