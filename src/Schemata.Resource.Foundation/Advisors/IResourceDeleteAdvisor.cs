using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on a delete operation
///     per <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso> with access to both the
///     entity
///     and the <see cref="DeleteRequest" />. Used for freshness checks
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IResourceDeleteAdvisor<TEntity> : IAdvisor<TEntity, DeleteRequest, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName;
