using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     The first advisor stage for a get request
///     per <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>. Advisors
///     authorize the caller and can modify the query via the
///     <see cref="ResourceRequestContainer{TEntity}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
public interface IResourceGetRequestAdvisor<TEntity, TDetail>
    : IAdvisor<GetRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?, HandledResponse<GetResultBase<TDetail>>>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName;
