using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceListRequestAdvice<TEntity> : IAdvice<ListRequest, HttpContext>
    where TEntity : class, IIdentifier;
