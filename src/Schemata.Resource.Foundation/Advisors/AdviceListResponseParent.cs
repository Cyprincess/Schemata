using System;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceListResponseParent{TSummary}" />.
/// </summary>
public static class AdviceListResponseParent
{
    /// <summary>
    ///     Default order anchored at <see cref="SchemataConstants.Orders.Base" /> so
    ///     <see cref="IChild.Parent" /> is populated on each summary before
    ///     <see cref="AdviceListResponseReadMask" /> trims fields off the list.
    /// </summary>
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

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
public sealed class AdviceListResponseParent<TSummary> : IResourceListResponseAdvisor<TSummary>
    where TSummary : class, ICanonicalName
{
    /// <inheritdoc cref="AdviceListResponseParent" />
    public int Order => AdviceListResponseParent.DefaultOrder;

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
