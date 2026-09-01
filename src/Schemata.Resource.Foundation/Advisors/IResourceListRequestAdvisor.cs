using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     The first advisor stage for a list request
///     per <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>. Advisors
///     authorize the caller and can shape the query.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IResourceListRequestAdvisor<TEntity> : IAdvisor<ListRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName;
