using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
/// Advises on the response after an entity has been mapped to a detail DTO.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type returned to the caller.</typeparam>
/// <remarks>
/// Invoked at the end of Get, Create, and Update operations after the entity has been mapped
/// to a detail DTO. Used for setting freshness ETags and caching idempotent responses.
/// Return <see cref="AdviseResult.Continue"/> to return the detail as-is,
/// <see cref="AdviseResult.Handle"/> to substitute a custom result, or
/// <see cref="AdviseResult.Block"/> to deny the response silently.
/// </remarks>
public interface IResourceResponseAdvisor<in TEntity, in TDetail> : IAdvisor<TEntity?, TDetail?, HttpContext?>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName;
