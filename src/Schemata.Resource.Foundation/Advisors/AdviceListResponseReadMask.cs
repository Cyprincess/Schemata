using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceListResponseReadMask{TSummary}" />.
/// </summary>
public static class AdviceListResponseReadMask
{
    /// <summary>
    ///     Default order: chained after <see cref="AdviceListResponseParent" />
    ///     so the parent canonical is set on each summary before a mask trims it off.
    /// </summary>
    public const int DefaultOrder = AdviceListResponseParent.DefaultOrder + 10_000_000;
}

/// <summary>
///     Trims each list summary to the fields named by the request's <c>read_mask</c>
///     per <seealso href="https://google.aip.dev/157">AIP-157: Partial responses</seealso>.
///     Runs only when the handler stashed a <see cref="ReadMaskRequested" /> marker.
/// </summary>
/// <typeparam name="TSummary">The summary DTO type.</typeparam>
public sealed class AdviceListResponseReadMask<TSummary> : IResourceListResponseAdvisor<TSummary>
    where TSummary : class, ICanonicalName
{
    #region IResourceListResponseAdvisor<TSummary> Members

    /// <inheritdoc cref="AdviceListResponseReadMask" />
    public int Order => AdviceListResponseReadMask.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext             ctx,
        ImmutableArray<TSummary>? summaries,
        ClaimsPrincipal?          principal,
        CancellationToken         ct = default
    ) {
        if (summaries is null || !ctx.TryGet<ReadMaskRequested>(out var mask) || mask is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        foreach (var summary in summaries) {
            AdviceResponseReadMask.Trim(summary, mask.Mask);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}