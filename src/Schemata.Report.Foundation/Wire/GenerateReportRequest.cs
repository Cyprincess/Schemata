using Schemata.Insight.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Wire request that starts a report generation operation.</summary>
public sealed class GenerateReportRequest
{
    /// <summary>Named report definition to generate; mutually exclusive with <see cref="Query" />.</summary>
    public string? Name { get; set; }

    /// <summary>Inline query to generate; mutually exclusive with <see cref="Name" />.</summary>
    public QueryInsightRequest? Query { get; set; }

    /// <summary>Whether the generated result is persisted as a report snapshot.</summary>
    public bool Persist { get; set; }

    /// <summary>Whether generation runs inline and returns a terminal operation.</summary>
    public bool Sync { get; set; }
}
