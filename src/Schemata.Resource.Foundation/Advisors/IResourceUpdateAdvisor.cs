using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on an update operation
///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso> with access to both the
///     request and the existing entity. Used for freshness checks
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public interface IResourceUpdateAdvisor<TEntity, TRequest> : IAdvisor<TRequest, TEntity, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName;
