using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;

namespace Schemata.Resource.Foundation;

/// <summary>Requests a preview or forced purge of soft-deleted resources.</summary>
/// <typeparam name="TEntity">The resource type that owns this dispatcher request.</typeparam>
public sealed class PurgeResourceRequest<TEntity> : ICommand<Operation>, IRequestPrincipal
{
    public string? Filter { get; set; }

    public string? Language { get; set; }

    public string? Parent { get; set; }

    public bool Force { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
