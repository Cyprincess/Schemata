using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on an update request
///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso> before the entity is
///     loaded.
///     Used by authorization and validation advisors.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public interface IResourceUpdateRequestAdvisor<TEntity, TRequest> : IAdvisor<TRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName;
