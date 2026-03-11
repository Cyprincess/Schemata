using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceDeleteAdvisor<TEntity> : IAdvisor<TEntity, DeleteRequest, HttpContext?>
    where TEntity : class, ICanonicalName;
