using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceBrowseAdvice<TEntity> : IAdvice<string, long, int, HttpContext>
    where TEntity : class, IIdentifier;
