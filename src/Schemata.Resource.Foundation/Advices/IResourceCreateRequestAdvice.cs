using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceCreateRequestAdvice<TEntity, TRequest> : IAdvice<TRequest, HttpContext>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier;
