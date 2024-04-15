using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceRequestAdvice<TEntity> : IAdvice<HttpContext, Operations>
    where TEntity : class, IIdentifier;
