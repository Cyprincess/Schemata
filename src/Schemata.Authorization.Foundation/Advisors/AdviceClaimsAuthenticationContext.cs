using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Claims advisor minting the authentication-event claims that the context resolved by
///     <see cref="IAuthenticationContextProvider" /> asserts onto the assembled claim set: the
///     principal's own <c>acr</c>, <c>amr</c>, and <c>auth_time</c> claims re-tagged for both
///     token destinations.
/// </summary>
/// <remarks>
///     <para>
///         <c>acr</c> and <c>amr</c> are OPTIONAL in the ID Token (OpenID Connect Core 1.0 §2)
///         and in the JWT access token (RFC 9068 §2.2.1); <c>auth_time</c> is REQUIRED there
///         when <c>max_age</c> was used, so it is minted whenever the context asserts it. The
///         <c>amr</c> claim carries the RFC 8176 method references as a JSON array
///         (<see cref="JsonClaimValueTypes.Json" />). The provider call runs at token-endpoint
///         claim assembly, so flows whose principal carries no evidence — an authorization-code
///         exchange above its bare subject/client claims — mint nothing; such flows receive the
///         context persisted beside the code payload.
///     </para>
///     <para>
///         A context with evidence is published on the ambient <see cref="AdviceContext" /> so
///         authorization-code creation can persist it; an absent or empty context mints no
///         claim (claim absence is legal per Core §2 and RFC 9068 §2.2.1). Without a
///         host-supplied provider the advisor mints nothing.
///     </para>
/// </remarks>
/// <seealso cref="AdviceClaimsAudience" />
/// <seealso cref="AdviceClaimsPairwise{TApp}" />
public sealed class AdviceClaimsAuthenticationContext(IAuthenticationContextProvider? contexts = null) : IClaimsAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceClaimsAudience.DefaultOrder + 10_000_000;

    #region IClaimsAdvisor Members

    public int Order => DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
        if (contexts is null) {
            return AdviseResult.Continue;
        }

        var context = await contexts.GetContextAsync(new(new ClaimsIdentity(claims)), ct);

        // Minted claims replace any bare copies the transport stamped, so a claim type is
        // never emitted twice.
        for (var i = claims.Count - 1; i >= 0; i--) {
            if (claims[i].Type is Claims.Acr or Claims.Amr or Claims.AuthTime) {
                claims.RemoveAt(i);
            }
        }

        var minted = false;
        if (!string.IsNullOrWhiteSpace(context.Acr)) {
            var acr = new Claim(Claims.Acr, context.Acr);
            acr.Properties[ClaimDestinations.IdentityToken] = Parameters.Token;
            acr.Properties[ClaimDestinations.AccessToken]   = Parameters.Token;
            claims.Add(acr);
            minted = true;
        }

        if (context.Amr is { Count: > 0 }) {
            var amr = new Claim(Claims.Amr, JsonSerializer.Serialize(context.Amr), JsonClaimValueTypes.Json);
            amr.Properties[ClaimDestinations.IdentityToken] = Parameters.Token;
            amr.Properties[ClaimDestinations.AccessToken]   = Parameters.Token;
            claims.Add(amr);
            minted = true;
        }

        if (context.AuthTime is not null) {
            // Core §2, RFC 9068 §2.2.1, and RFC 9470 §6.2 all type auth_time as a JSON number.
            var authTime = new Claim(
                Claims.AuthTime,
                context.AuthTime.Value.ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64);
            authTime.Properties[ClaimDestinations.IdentityToken] = Parameters.Token;
            authTime.Properties[ClaimDestinations.AccessToken]   = Parameters.Token;
            claims.Add(authTime);
            minted = true;
        }

        if (minted) {
            ctx.Set(context);
        }

        return AdviseResult.Continue;
    }

    #endregion
}
