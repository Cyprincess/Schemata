using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceResponseAdvice<TEntity> : IAdvice<TEntity, HttpContext>
    where TEntity : class, IIdentifier;
