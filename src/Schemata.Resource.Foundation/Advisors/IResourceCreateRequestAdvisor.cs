using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceCreateRequestAdvisor<TEntity, TRequest> : IAdvisor<TRequest, HttpContext?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName;
