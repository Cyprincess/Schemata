using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

public interface IResourceListResponseAdvisor<TSummary> : IAdvisor<ImmutableArray<TSummary>?, HttpContext?>
    where TSummary : class, IIdentifier;
