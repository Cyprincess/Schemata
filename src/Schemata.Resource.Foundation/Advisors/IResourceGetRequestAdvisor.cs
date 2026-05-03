using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on a get request
///     per <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso> after the general
///     <see cref="IResourceRequestAdvisor{TEntity}" /> has run. Advisors can modify the query
///     via the <see cref="ResourceRequestContainer{TEntity}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IResourceGetRequestAdvisor<TEntity> : IAdvisor<GetRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName;
