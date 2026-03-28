using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on a Create operation after the entity has been mapped from the request but before persistence.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
/// <remarks>
///     Invoked during
///     <see cref="ResourceOperationHandler{TEntity, TRequest, TDetail, TSummary}.CreateAsync">CreateAsync</see> after the
///     request has been mapped to an entity and parent
///     properties have been set from route values. Advisors can inspect or modify the entity
///     before it is persisted. Return <see cref="AdviseResult.Continue" /> to proceed,
///     <see cref="AdviseResult.Handle" /> to return a pre-built result, or
///     <see cref="AdviseResult.Block" /> to deny the operation.
/// </remarks>
public interface IResourceCreateAdvisor<TEntity, TRequest> : IAdvisor<TRequest, TEntity, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName;
