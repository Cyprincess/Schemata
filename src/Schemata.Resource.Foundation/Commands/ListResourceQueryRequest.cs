using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;

namespace Schemata.Resource.Foundation.Commands;

/// <summary>Requests a page of resources through the standard Resource advisor pipeline.</summary>
/// <param name="Request">The AIP-132 list request.</param>
/// <param name="Principal">The caller associated with the operation.</param>
/// <typeparam name="TEntity">The persistent entity type identifying the resource registration.</typeparam>
/// <typeparam name="TSummary">The resource summary response type.</typeparam>
public sealed record ListResourceQueryRequest<TEntity, TSummary>(ListRequest Request, ClaimsPrincipal? Principal)
    : IQuery<ListResultBase<TSummary>>, IRequestPrincipal
    where TEntity : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}
