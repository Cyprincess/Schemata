using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceResponseAdvice<TEntity, TDetail> : IAdvice<TEntity?, TDetail?, HttpContext>
    where TEntity : class, IIdentifier
    where TDetail : class, IIdentifier;
