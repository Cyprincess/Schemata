using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceDeleteAdvisor<TEntity> : IAdvisor<long, TEntity, HttpContext?>
    where TEntity : class, IIdentifier;
