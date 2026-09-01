using System;
using Schemata.Insight.Skeleton.Models;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Skeleton.Advisors;

/// <summary>Mutable objects supplied to report-snapshot advisors.</summary>
public sealed class ReportSnapshotContext
{
    /// <summary>Initializes the report-snapshot advisory context.</summary>
    /// <param name="snapshot">The persisted snapshot header being finalized.</param>
    /// <param name="response">The materialized report response.</param>
    public ReportSnapshotContext(SchemataReportSnapshot snapshot, QueryInsightResponse response) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(response);
        Snapshot = snapshot;
        Response = response;
    }

    /// <summary>The snapshot header available for metadata changes.</summary>
    public SchemataReportSnapshot Snapshot { get; }

    /// <summary>The materialized response available for result changes.</summary>
    public QueryInsightResponse Response { get; }
}