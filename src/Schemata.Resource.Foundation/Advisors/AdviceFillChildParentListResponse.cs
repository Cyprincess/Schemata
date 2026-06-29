using System;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Derives <see cref="IChild.Parent" /> on each list summary from the summary's
///     own <see cref="ICanonicalName.CanonicalName" />.
/// </summary>
/// <remarks>
///     The advisor short-circuits at sort time when
///     <typeparamref name="TSummary" /> does not implement <see cref="IChild" />.
///     Each element is mutated in place — the immutable array is not replaced.
/// </remarks>
/// <typeparam name="TSummary">The summary DTO type; the advisor fires only when it implements <see cref="IChild" />.</typeparam>
public sealed class AdviceFillChildParentListResponse<TSummary> : IResourceListResponseAdvisor<TSummary>
    where TSummary : class, ICanonicalName
{
    /// <summary>
    ///     Default order: piggybacks on the per-item response advisor's slot.
    /// </summary>
    public int Order => AdviceFillChildParentResponse.DefaultOrder;

    #region IResourceListResponseAdvisor<TSummary> Members

    public Task<AdviseResult> AdviseAsync(
        AdviceContext             ctx,
        ImmutableArray<TSummary>? summaries,
        ClaimsPrincipal?          principal,
        CancellationToken         ct = default
    ) {
        if (!typeof(IChild).IsAssignableFrom(typeof(TSummary)) || summaries is not { } array) {
            return Task.FromResult(AdviseResult.Continue);
        }

        foreach (var summary in array) {
            if (summary is not IChild child) {
                continue;
            }

            var parent = ChildParentHelper.DeriveParent(summary.CanonicalName);
            if (!string.Equals(child.Parent, parent, StringComparison.Ordinal)) {
                child.Parent = parent;
            }
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
