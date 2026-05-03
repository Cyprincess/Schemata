using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on a list request
///     per <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso> after the general
///     <see cref="IResourceRequestAdvisor{TEntity}" /> has run.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IResourceListRequestAdvisor<TEntity> : IAdvisor<ListRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName;
