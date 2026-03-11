using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceDeleteRequestAdvisor<TEntity> : IAdvisor<long, HttpContext?>
    where TEntity : class, IIdentifier;
