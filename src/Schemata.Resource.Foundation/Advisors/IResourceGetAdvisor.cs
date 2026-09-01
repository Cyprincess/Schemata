using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceGetAdvisor<TEntity> : IAdvisor<GetRequest, TEntity, ClaimsPrincipal?>
    where TEntity : class, ICanonicalName;
