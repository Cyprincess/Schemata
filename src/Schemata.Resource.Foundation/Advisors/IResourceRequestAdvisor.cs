using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceRequestAdvisor<TEntity> : IAdvisor<HttpContext?, Operations>
    where TEntity : class, ICanonicalName;
