using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
/// Advises on a List request after the general request advisor has run.
/// </summary>
/// <typeparam name="TEntity">The entity type being listed.</typeparam>
/// <remarks>
/// Invoked during <see cref="ResourceOperationHandler{TEntity, TRequest, TDetail, TSummary}.ListAsync">ListAsync</see> after <see cref="IResourceRequestAdvisor{TEntity}"/>.
/// Advisors can inspect the <see cref="ListRequest"/>, modify the query via
/// <see cref="ResourceRequestContainer{T}"/>, or short-circuit the operation.
/// Return <see cref="AdviseResult.Continue"/> to proceed, <see cref="AdviseResult.Handle"/> to return a
/// pre-built result, or <see cref="AdviseResult.Block"/> to deny the request silently.
/// </remarks>
public interface IResourceListRequestAdvisor<TEntity> : IAdvisor<ListRequest, ResourceRequestContainer<TEntity>, HttpContext?>
    where TEntity : class, ICanonicalName;
