using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Models;
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
        Schemata.Authorization.Skeleton.Models.AuthorizationClaimContext issuance,
        CancellationToken ct = default
    ) {
        if (claim.Type == Claims.UserinfoRequest || claim.Type == Claims.ClientId) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var requested = principal.FindFirstValue(Claims.UserinfoRequest);
        if (requested is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        // A malformed persisted request is a configuration error, not a skippable claim: surface
        // it the same way the sign-in service does when it writes the field.
        var specs = ClaimsRequest.ParseUserinfo(requested);

        // §5.5.1: a value/values qualifier is an equality gate on the final claim value; a
        // mismatching claim is omitted from the requested destination.
        if (specs?.TryGetValue(claim.Type, out var spec) == true && (spec?.Matches(claim.Value) ?? true)) {
            destinations.Add(ClaimDestinations.UserInfo);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
