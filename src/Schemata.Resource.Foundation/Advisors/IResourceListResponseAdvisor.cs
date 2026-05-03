using System.Collections.Immutable;
using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on the list response after entities have been queried and mapped to summaries
///     per <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>. Advisors receive the
///     immutable array of summaries and can inspect or replace the result.
/// </summary>
/// <typeparam name="TSummary">The summary DTO type.</typeparam>
public interface IResourceListResponseAdvisor<TSummary> : IAdvisor<ImmutableArray<TSummary>?, ClaimsPrincipal?>
    where TSummary : class, ICanonicalName;
