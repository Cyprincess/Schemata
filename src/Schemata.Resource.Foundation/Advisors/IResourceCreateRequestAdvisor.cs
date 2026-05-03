using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on a create request
///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso> before the entity is
///     mapped.
///     Used by authorization, idempotency, and validation advisors.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public interface IResourceCreateRequestAdvisor<TEntity, TRequest> : IAdvisor<TRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName;
