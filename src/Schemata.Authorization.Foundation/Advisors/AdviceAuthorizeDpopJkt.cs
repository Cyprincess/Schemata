using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeDpopJkt{TApp}" />.</summary>
public static class AdviceAuthorizeDpopJkt
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeNonce.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates the <c>dpop_jkt</c> authorization request parameter, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-10">
///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
///         (DPoP) §10: Authorization Code Binding to a DPoP Key
///     </seealso>
///     . The value is the RFC 7638 SHA-256 JWK thumbprint of the client's proof-of-possession
///     public key — base64url decoding to exactly 32 bytes. Requests without the parameter pass
///     through unbound; the authorization code handler enforces the committed key at exchange.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     The §10.1 PAR co-existence rule (a DPoP header on a pushed authorization request behaves
///     as if its thumbprint were supplied via <c>dpop_jkt</c>, including the mismatch rejection)
///     requires PAR support and is deferred until PAR lands.
/// </remarks>
public sealed class AdviceAuthorizeDpopJkt<TApp> : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeDpopJkt.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        var jkt = authz.Request?.DpopJkt;
        if (string.IsNullOrWhiteSpace(jkt)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        // §10: the parameter value is an RFC 7638 SHA-256 JWK thumbprint — base64url decoding
        // to exactly 32 bytes. The decoder signals malformed input with FormatException.
        byte[] decoded;
        try {
            decoded = Base64UrlEncoder.DecodeBytes(jkt);
        } catch (FormatException) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.DPOP_JKT_MALFORMED)
            );
        }

        if (decoded.Length != 32) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.DPOP_JKT_MALFORMED)
            );
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
