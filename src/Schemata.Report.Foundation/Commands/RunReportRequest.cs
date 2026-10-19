using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Skeleton.Enums;
using Schemata.Report.Skeleton.Models;

namespace Schemata.Report.Foundation.Commands;

/// <summary>Requests inline or persisted report execution through the Report pipeline.</summary>
/// <param name="Request">The named or inline report request.</param>
/// <param name="Principal">The caller used for source-level authorization.</param>
public sealed record RunReportRequest(ReportRequest Request, ClaimsPrincipal? Principal)
    : ICommand<ReportResult>, IRequestPrincipal, IReportScoped
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;

    [JsonIgnore]
    public string? ReportKey => Request?.Name;

    [JsonIgnore]
    public ReportRunKind Kind { get; init; } = ReportRunKind.ImmediatePersisted;

    [JsonIgnore]
    public string? Operation { get; init; }

    [JsonIgnore]
    public Func<CancellationToken, ValueTask<bool>>? IsCancelled { get; init; }
}
