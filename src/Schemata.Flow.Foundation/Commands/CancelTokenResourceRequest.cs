using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

/// <summary>Identifies one process token to cancel.</summary>
public sealed class CancelTokenResourceRequest : ICanonicalName, ICommand<ProcessSnapshot>, IRequestPrincipal
{
    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
