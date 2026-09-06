using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;
public sealed class AdviceDestinationUserinfoRequest : IDestinationAdvisor
{
    /// <summary>
    ///     The default advisor ordering value. Runs first: destination advisors may finish a
    ///     claim with <c>Handle</c>, and the §5.5 addition must already be in the set by then.
    /// </summary>
    public const int DefaultOrder = Orders.Base - 10_000_000;

    /// <summary>The default advisor ordering value.</summary>
    public int Order => DefaultOrder;

    #region IDestinationAdvisor Members

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        Claim             claim,
        HashSet<string>   destinations,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        if (claim.Type == Claims.UserinfoRequest || claim.Type == Claims.ClientId) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var requested = principal.FindFirstValue(Claims.UserinfoRequest);
        if (requested is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var names = requested.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (Array.IndexOf(names, claim.Type) >= 0) {
            destinations.Add(ClaimDestinations.UserInfo);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
