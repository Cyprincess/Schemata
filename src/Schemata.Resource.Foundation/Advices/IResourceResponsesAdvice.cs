using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceResponsesAdvice<TSummary> : IAdvice<IEnumerable<TSummary>?, HttpContext>
    where TSummary : class, IIdentifier;
