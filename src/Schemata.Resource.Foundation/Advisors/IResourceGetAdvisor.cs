using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceGetAdvisor<TEntity, TDetail>
    : IAdvisor<GetRequest, TEntity, ClaimsPrincipal?, HandledResponse<GetResultBase<TDetail>>>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName;
