using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceEditAdvice<TEntity, TRequest> : IAdvice<long, TRequest, TEntity, HttpContext>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier;
