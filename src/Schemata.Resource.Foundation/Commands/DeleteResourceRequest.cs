using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;

namespace Schemata.Resource.Foundation.Commands;

/// <summary>Requests deletion of a resource through the standard Resource advisor pipeline.</summary>
/// <param name="Name">The canonical name of the resource to delete.</param>
/// <param name="Etag">The optional freshness ETag.</param>
/// <param name="Principal">The caller associated with the operation.</param>
/// <param name="AllowMissing">Whether a missing resource produces a successful empty result.</param>
/// <typeparam name="TEntity">The persistent entity type identifying the resource registration.</typeparam>
/// <typeparam name="TDetail">The resource detail response type used by soft deletion.</typeparam>
public sealed record DeleteResourceRequest<TEntity, TDetail>(
    string           Name,
    string?          Etag,
    ClaimsPrincipal? Principal,
    bool             AllowMissing = false
) : ICommand<DeleteResultBase<TDetail>>, IRequestPrincipal
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}
