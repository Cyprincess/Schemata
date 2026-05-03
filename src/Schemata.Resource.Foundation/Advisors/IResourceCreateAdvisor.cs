using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on a create operation
///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso> after the entity has been
///     mapped from the request but before persistence. Advisors can inspect or modify the entity.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public interface IResourceCreateAdvisor<TEntity, TRequest> : IAdvisor<TRequest, TEntity, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName;
