using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on a Get request after the general request advisor has run.
/// </summary>
/// <typeparam name="TEntity">The entity type being retrieved.</typeparam>
/// <remarks>
///     Invoked during <see cref="ResourceOperationHandler{TEntity, TRequest, TDetail, TSummary}.GetAsync">GetAsync</see>
///     after <see cref="IResourceRequestAdvisor{TEntity}" />.
///     Advisors receive the <see cref="GetRequest" /> containing the resource name and can modify the query
///     via <see cref="ResourceRequestContainer{T}" />.
///     Return <see cref="AdviseResult.Continue" /> to proceed, <see cref="AdviseResult.Handle" /> to return a
///     pre-built result, or <see cref="AdviseResult.Block" /> to deny the request silently.
/// </remarks>
public interface IResourceGetRequestAdvisor<TEntity> : IAdvisor<GetRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName;
