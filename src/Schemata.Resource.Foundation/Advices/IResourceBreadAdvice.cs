using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceBreadAdvice<TEntity> : IAdvice<HttpContext>
    where TEntity : class, IIdentifier;
