using System;
using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Insight.Skeleton;

namespace Schemata.Report.Skeleton;

/// <summary>Runs before report definition resolution; implementations return <see cref="AdviseResult.Continue" /> to continue generation, and thrown exceptions abort it.</summary>
public interface IReportGenerateAdvisor : IAdvisor<ReportGenerateContext>
{
}

/// <summary>Runs after report definition resolution and before Insight planning; implementations return <see cref="AdviseResult.Continue" /> to continue generation, and thrown exceptions abort it.</summary>
public interface IReportDefinitionAdvisor : IAdvisor<ReportDefinitionContext>
{
}

/// <summary>Runs after report materialization and before persisted snapshot finalization; implementations return <see cref="AdviseResult.Continue" /> to continue generation, and thrown exceptions abort it.</summary>
public interface IReportSnapshotAdvisor : IAdvisor<ReportSnapshotContext>
{
}

/// <summary>Mutable state supplied to report-generation advisors.</summary>
public sealed class ReportGenerateContext
{
    /// <summary>Initializes the generation context.</summary>
    /// <param name="request">The named or inline report request.</param>
    /// <param name="report">The named report, or <see langword="null" /> for an inline request.</param>
    /// <param name="kind">The immediate or scheduled execution kind.</param>
    /// <param name="principal">The caller principal, or <see langword="null" /> for dispatched and scheduled runs.</param>
    public ReportGenerateContext(ReportRequest request, string? report, ReportRunKind kind, ClaimsPrincipal? principal) {
        ArgumentNullException.ThrowIfNull(request);
        Request   = request;
        Report    = report;
        Kind      = kind;
        Principal = principal;
    }

    /// <summary>The mutable named or inline report request.</summary>
    public ReportRequest Request { get; }

    /// <summary>The named report, or <see langword="null" /> for an inline request.</summary>
    public string? Report { get; }

    /// <summary>The immediate or scheduled execution kind.</summary>
    public ReportRunKind Kind { get; }

    /// <summary>The principal the materialization runs under; initialized from the caller and replaceable by advisors.</summary>
    public ClaimsPrincipal? Principal { get; set; }
}

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
