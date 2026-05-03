using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on the response after an entity has been mapped to a detail DTO.
///     Used for setting freshness ETags
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso> and caching
///     idempotent
///     responses per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
public interface IResourceResponseAdvisor<in TEntity, in TDetail> : IAdvisor<TEntity?, TDetail?, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName;
