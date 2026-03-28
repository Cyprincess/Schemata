using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on a Delete request before the entity is deleted.
/// </summary>
/// <typeparam name="TEntity">The entity type being deleted.</typeparam>
/// <remarks>
///     Invoked during
///     <see cref="ResourceOperationHandler{TEntity, TRequest, TDetail, TSummary}.DeleteAsync">DeleteAsync</see> after
///     <see cref="IResourceRequestAdvisor{TEntity}" />.
///     Advisors receive the <see cref="DeleteRequest" /> containing the resource name, etag, and force flag,
///     and can modify the query via <see cref="ResourceRequestContainer{T}" />.
///     Return <see cref="AdviseResult.Continue" /> to proceed, <see cref="AdviseResult.Handle" /> to indicate
///     success without performing the delete, or <see cref="AdviseResult.Block" /> to deny the request.
/// </remarks>
public interface IResourceDeleteRequestAdvisor<TEntity> : IAdvisor<DeleteRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName;
