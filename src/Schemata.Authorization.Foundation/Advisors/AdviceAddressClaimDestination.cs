using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Extensions;
using Schemata.Authorization.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Claim destination advisor for the <c>address</c> claim, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#IndividualClaimsLanguages">
///         OpenID Connect Core 1.0 §5.5.2:
///         Languages and Scripts for Individual Claims
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     Always includes the claim in access tokens. If the <c>address</c> scope was granted,
///     also includes it in identity tokens and UserInfo responses.
/// </remarks>
/// <seealso cref="AdvicePhoneClaimDestination" />
public sealed class AdviceAddressClaimDestination : IDestinationAdvisor
{
    public const int DefaultOrder = AdvicePhoneClaimDestination.DefaultOrder + 10_000_000;

    #region IDestinationAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        Claim             claim,
        HashSet<string>   destinations,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        if (claim.Type is not Claims.Address) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!principal.HasScope(Scopes.Address)) {
            return Task.FromResult(AdviseResult.Handle);
        }

        destinations.Add(ClaimDestinations.IdentityToken);
        destinations.Add(ClaimDestinations.UserInfo);

        return Task.FromResult(AdviseResult.Handle);
    }

    #endregion
}
