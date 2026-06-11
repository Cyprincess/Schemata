using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

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

    public int Order => AdviceResponseReadMask.DefaultOrder;

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