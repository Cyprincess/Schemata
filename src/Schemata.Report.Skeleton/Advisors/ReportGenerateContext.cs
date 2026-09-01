using System;
using System.Security.Claims;
using Schemata.Report.Skeleton.Enums;
using Schemata.Report.Skeleton.Models;

namespace Schemata.Report.Skeleton.Advisors;

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