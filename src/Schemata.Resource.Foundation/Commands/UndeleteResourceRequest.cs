using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Resource.Foundation.Commands;

/// <summary>Identifies one soft-deleted resource to restore.</summary>
/// <typeparam name="TEntity">The resource type that owns this dispatcher request.</typeparam>
/// <typeparam name="TDetail">The restored resource detail response type.</typeparam>
public sealed class UndeleteResourceRequest<TEntity, TDetail> : ICommand<TDetail>, ICanonicalName, IRequestPrincipal
    where TDetail : class, ICanonicalName
{
    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
