using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceResponsesAdvice<TEntity> : IAdvice<IList<TEntity>, HttpContext>
    where TEntity : class, IIdentifier;
