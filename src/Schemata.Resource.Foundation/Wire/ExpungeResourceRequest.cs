using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;

namespace Schemata.Resource.Foundation;

/// <summary>Identifies one soft-deleted resource to remove permanently.</summary>
/// <typeparam name="TEntity">The resource type that owns this dispatcher request.</typeparam>
public sealed class ExpungeResourceRequest<TEntity> : ICommand<EmptyResourceResponse>, ICanonicalName, IRequestPrincipal
{
    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
