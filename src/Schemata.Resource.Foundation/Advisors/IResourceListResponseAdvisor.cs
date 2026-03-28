using System.Collections.Immutable;
using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on the list response after entities have been queried and mapped to summaries.
/// </summary>
/// <typeparam name="TSummary">The summary DTO type returned in list results.</typeparam>
/// <remarks>
///     Invoked at the end of
///     <see cref="ResourceOperationHandler{TEntity, TRequest, TDetail, TSummary}.ListAsync">ListAsync</see>, after query
///     execution and mapping.
///     Advisors receive the immutable array of summaries and can inspect or replace the result.
///     Return <see cref="AdviseResult.Continue" /> to return the summaries as-is,
///     <see cref="AdviseResult.Handle" /> to substitute a custom result, or
///     <see cref="AdviseResult.Block" /> to deny the response silently.
/// </remarks>
public interface IResourceListResponseAdvisor<TSummary> : IAdvisor<ImmutableArray<TSummary>?, ClaimsPrincipal?>
    where TSummary : class, ICanonicalName;
