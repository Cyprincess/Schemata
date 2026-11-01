using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using static Schemata.Abstractions.SchemataConstants;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceDestinationClaimsRequest : IDestinationAdvisor
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
        // §5.5.1: a value/values qualifier is an equality gate on the final claim value; a
        // mismatching claim is omitted from the requested destination.
        if (issuance.RequestedClaims?.IdToken is { Count: > 0 } requested
         && requested.TryGetValue(claim.Type, out var spec)
         && (spec?.Matches(claim.Value) ?? true)) {
            destinations.Add(ClaimDestinations.IdentityToken);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
