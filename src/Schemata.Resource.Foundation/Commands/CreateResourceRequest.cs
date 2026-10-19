using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Resource.Foundation.Commands;

/// <summary>Requests creation of a resource through the standard Resource advisor pipeline.</summary>
/// <param name="Request">The resource creation payload.</param>
/// <param name="Principal">The caller associated with the operation.</param>
/// <typeparam name="TEntity">The persistent entity type identifying the resource registration.</typeparam>
/// <typeparam name="TRequest">The resource request DTO type.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed record CreateResourceRequest<TEntity, TRequest, TDetail>(TRequest Request, ClaimsPrincipal? Principal)
    : ICommand<CreateResultBase<TDetail>>, IRequestPrincipal, IResourceDispatchCarrier
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;

    /// <summary>The identified dispatch execution attached by the idempotency wrap.</summary>
    [JsonIgnore]
    public ResourceDispatchExecution? Dispatch { get; set; }
}
