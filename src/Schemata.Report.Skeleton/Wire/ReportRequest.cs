using Schemata.Insight.Skeleton;

namespace Schemata.Report.Skeleton;

/// <summary>Request to run either a named report definition or an inline insight query.</summary>
public sealed class ReportRequest
{
    /// <summary>Named report definition to run; exactly one of this property and <see cref="Query" /> is required.</summary>
    public string? Name { get; set; }

    /// <summary>Inline query to run; exactly one of this property and <see cref="Name" /> is required.</summary>
    public QueryInsightRequest? Query { get; set; }

    /// <summary>Indicates whether the result is persisted as a snapshot.</summary>
    public bool Persist { get; set; }
}
