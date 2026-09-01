using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Messaging.Skeleton;
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
}
