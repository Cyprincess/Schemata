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
///     Claim destination advisor for the <c>phone_number</c> and <c>phone_number_verified</c> claims, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#IndividualClaimsLanguages">
///         OpenID Connect Core 1.0 §5.5.2:
///         Languages and Scripts for Individual Claims
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     Always includes the claim in access tokens. If the <c>phone</c> scope was granted,
///     also includes it in identity tokens and UserInfo responses.
/// </remarks>
/// <seealso cref="AdviceEmailClaimDestination" />
public sealed class AdvicePhoneClaimDestination : IDestinationAdvisor
{
    public const int DefaultOrder = AdviceEmailClaimDestination.DefaultOrder + 10_000_000;

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
        switch (claim.Type) {
            case Claims.PhoneNumber:
            case Claims.PhoneNumberVerified:
                if (!principal.HasScope(Scopes.Phone)) {
                    return Task.FromResult(AdviseResult.Handle);
                }

                destinations.Add(ClaimDestinations.IdentityToken);
                destinations.Add(ClaimDestinations.UserInfo);

                return Task.FromResult(AdviseResult.Handle);
            default:
                return Task.FromResult(AdviseResult.Continue);
        }
    }

    #endregion
}
