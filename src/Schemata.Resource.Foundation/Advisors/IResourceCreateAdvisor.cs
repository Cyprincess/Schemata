using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceCreateAdvisor<TEntity, TRequest> : IAdvisor<TRequest, TEntity, HttpContext?>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName;
