using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advices;

public interface IResourceResponsesAdvice<TSummary> : IAdvice<ImmutableArray<TSummary>?, HttpContext>
    where TSummary : class, IIdentifier;
