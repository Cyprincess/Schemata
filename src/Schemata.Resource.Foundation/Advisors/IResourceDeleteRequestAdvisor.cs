using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on a delete request
///     per <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso> before the entity is
///     loaded.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IResourceDeleteRequestAdvisor<TEntity> : IAdvisor<DeleteRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName;
