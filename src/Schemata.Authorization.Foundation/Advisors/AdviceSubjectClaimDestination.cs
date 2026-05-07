using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

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
///     <c>aud</c> goes to access and identity tokens.
/// </remarks>
/// <seealso cref="AdviceProfileClaimDestination" />
public sealed class AdviceSubjectClaimDestination : IDestinationAdvisor
{
    public const int DefaultOrder = Orders.Base;

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
            case Claims.Subject:
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
            default:
                return Task.FromResult(AdviseResult.Continue);
        }
    }

    #endregion
}
