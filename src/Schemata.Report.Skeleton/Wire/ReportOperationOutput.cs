using Schemata.Insight.Skeleton;

namespace Schemata.Report.Skeleton;

/// <summary>Serialized result payload for a completed report-generation operation.</summary>
/// <remarks>
///     This is a discriminated payload: persisted generations set <see cref="Snapshot" />, while ephemeral
///     generations set <see cref="Response" />. Exactly one branch must be present.
/// </remarks>
public sealed class ReportOperationOutput
{
    /// <summary>The canonical name of the generated snapshot for a persisted report.</summary>
    public string? Snapshot { get; set; }

    /// <summary>The inline insight response for an ephemeral report.</summary>
    public QueryInsightResponse? Response { get; set; }
}
