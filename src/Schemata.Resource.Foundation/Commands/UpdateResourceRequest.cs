using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;

namespace Schemata.Resource.Foundation.Commands;

/// <summary>Requests update of a resource through the standard Resource advisor pipeline.</summary>
/// <param name="Name">The canonical name of the resource to update.</param>
/// <param name="Request">The resource update payload.</param>
/// <param name="Principal">The caller associated with the operation.</param>
/// <typeparam name="TEntity">The persistent entity type identifying the resource registration.</typeparam>
/// <typeparam name="TRequest">The resource request DTO type.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed record UpdateResourceRequest<TEntity, TRequest, TDetail>(string Name, TRequest Request, ClaimsPrincipal? Principal)
    : ICommand<UpdateResultBase<TDetail>>, IRequestPrincipal
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}
