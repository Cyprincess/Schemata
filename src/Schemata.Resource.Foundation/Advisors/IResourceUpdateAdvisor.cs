using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
/// Advises on an Update operation with access to both the request and the existing entity.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying update data.</typeparam>
/// <remarks>
/// Invoked during <see cref="ResourceOperationHandler{TEntity, TRequest, TDetail, TSummary}.UpdateAsync">UpdateAsync</see> after <see cref="IResourceUpdateRequestAdvisor{TEntity, TRequest}"/>
/// and before the request is mapped onto the entity. Used for freshness checks (ETag validation).
/// Return <see cref="AdviseResult.Continue"/> to proceed, <see cref="AdviseResult.Handle"/> to return a
/// pre-built result, or <see cref="AdviseResult.Block"/> to deny the operation.
/// </remarks>
public interface IResourceUpdateAdvisor<TEntity, TRequest> : IAdvisor<TRequest, TEntity, HttpContext?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName;
