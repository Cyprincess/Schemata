using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceUpdateRequestAdvisor<TEntity, TRequest> : IAdvisor<TRequest, HttpContext?>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier;
