using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Resource;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Messaging.Skeleton;

namespace Schemata.Report.Foundation.Commands;

public sealed class GenerateReportRequest : ICommand<Operation>, IRequestPrincipal, IReportScoped
{
    /// <summary>Named report definition to generate; mutually exclusive with <see cref="Query" />.</summary>
    public string? Name { get; set; }

    /// <summary>Inline query to generate; mutually exclusive with <see cref="Name" />.</summary>
    public QueryInsightRequest? Query { get; set; }

    /// <summary>Whether the generated result is persisted as a report snapshot.</summary>
    public bool Persist { get; set; }

    /// <summary>Whether generation runs inline and returns a terminal operation.</summary>
    public bool Sync { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    [JsonIgnore]
    public string? ReportKey => Name;
}
