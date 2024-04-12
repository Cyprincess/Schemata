using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceDeleteAdvice<TEntity> : IAdvice<long, HttpContext>
    where TEntity : class, IIdentifier;
