using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Claim destination advisor for the <c>sub</c>, <c>client_id</c>, and <c>aud</c> claims,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#IndividualClaimsLanguages">
///         OpenID Connect Core 1.0 §5.5.2:
///         Languages and Scripts for Individual Claims
///     </seealso>
///     and
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.3: Access Token Request
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     <c>sub</c> always goes to all three destinations (access token, id token, userinfo).
///     <c>client_id</c> goes to access tokens only.
///     A claim already carrying a destination property owns its routing and is left untouched;
///     this advisor only assigns destinations to untagged claims.
/// </remarks>
/// <seealso cref="AdviceClaimsAudience" />
/// <seealso cref="AdviceDestinationProfile" />
public sealed class AdviceDestinationSubject : IDestinationAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = Orders.Base;

    #region IDestinationAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        Claim             claim,
        HashSet<string>   destinations,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        if (claim.Properties.ContainsKey(ClaimDestinations.AccessToken)
         || claim.Properties.ContainsKey(ClaimDestinations.IdentityToken)
         || claim.Properties.ContainsKey(ClaimDestinations.UserInfo)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        switch (claim.Type) {
            case IdentityClaims.Subject:
                destinations.Add(ClaimDestinations.AccessToken);

                destinations.Add(ClaimDestinations.IdentityToken);
                destinations.Add(ClaimDestinations.UserInfo);

                return Task.FromResult(AdviseResult.Handle);
            case Claims.ClientId:
                destinations.Add(ClaimDestinations.AccessToken);

                return Task.FromResult(AdviseResult.Handle);
            case Claims.Audience:
                destinations.Add(ClaimDestinations.AccessToken);

                destinations.Add(ClaimDestinations.IdentityToken);

                return Task.FromResult(AdviseResult.Handle);
            case Claims.Scope:
                destinations.Add(ClaimDestinations.AccessToken);

                destinations.Add(ClaimDestinations.IdentityToken);

                return Task.FromResult(AdviseResult.Handle);
            case Claims.Resources:
                destinations.Add(ClaimDestinations.AccessToken);

                return Task.FromResult(AdviseResult.Handle);
            default:
                return Task.FromResult(AdviseResult.Continue);
        }
    }

    #endregion
}
