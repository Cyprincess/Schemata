using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on an Update request before the entity is modified.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying update data.</typeparam>
/// <remarks>
///     Invoked during
///     <see cref="ResourceOperationHandler{TEntity, TRequest, TDetail, TSummary}.UpdateAsync">UpdateAsync</see> after
///     <see cref="IResourceRequestAdvisor{TEntity}" /> and before
///     <see cref="IResourceUpdateAdvisor{TEntity, TRequest}" />. Used for authorization and validation.
///     The <see cref="ResourceRequestContainer{T}" /> is available for query modification.
///     Return <see cref="AdviseResult.Continue" /> to proceed, <see cref="AdviseResult.Handle" /> to return a
///     pre-built result, or <see cref="AdviseResult.Block" /> to deny the request.
/// </remarks>
public interface IResourceUpdateRequestAdvisor<TEntity, TRequest> : IAdvisor<TRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName;
