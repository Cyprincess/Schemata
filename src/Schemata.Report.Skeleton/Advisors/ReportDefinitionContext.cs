using System;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Skeleton.Advisors;

/// <summary>Mutable state supplied to report-definition advisors.</summary>
public sealed class ReportDefinitionContext
{
    /// <summary>Initializes the report-definition advisory context.</summary>
    /// <param name="query">The resolved query definition.</param>
    /// <param name="report">The resolved report definition, or <see langword="null" /> for an inline query.</param>
    public ReportDefinitionContext(QueryInsightRequest query, SchemataReport? report) {
        ArgumentNullException.ThrowIfNull(query);
        Query  = query;
        Report = report;
    }

    /// <summary>The query definition that advisors can replace before Insight planning.</summary>
    public QueryInsightRequest Query { get; set; }

    /// <summary>The resolved report definition, or <see langword="null" /> for an inline query.</summary>
    public SchemataReport? Report { get; }
}