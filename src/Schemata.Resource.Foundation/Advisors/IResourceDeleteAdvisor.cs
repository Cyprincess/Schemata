using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
/// Advises on a Delete operation with access to both the entity and the delete request.
/// </summary>
/// <typeparam name="TEntity">The entity type being deleted.</typeparam>
/// <remarks>
/// Invoked during <see cref="ResourceOperationHandler{TEntity, TRequest, TDetail, TSummary}.DeleteAsync">DeleteAsync</see> after <see cref="IResourceDeleteRequestAdvisor{TEntity}"/>
/// and before the entity is removed from the repository. Used for freshness checks (ETag validation).
/// Return <see cref="AdviseResult.Continue"/> to proceed, <see cref="AdviseResult.Handle"/> to indicate
/// success without deleting, or <see cref="AdviseResult.Block"/> to deny the operation.
/// </remarks>
public interface IResourceDeleteAdvisor<TEntity> : IAdvisor<TEntity, DeleteRequest, HttpContext?>
    where TEntity : class, ICanonicalName;
