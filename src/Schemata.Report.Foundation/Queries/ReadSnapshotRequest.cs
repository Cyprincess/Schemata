using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Report.Foundation.Queries;

public sealed class ReadSnapshotRequest : ICanonicalName, IQuery<ReadSnapshotResponse>, IRequestPrincipal
{
    public int? PageSize { get; set; }

    public string? PageToken { get; set; }

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
