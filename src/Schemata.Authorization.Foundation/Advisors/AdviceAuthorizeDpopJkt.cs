using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Services;
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
///     On the pushed authorization request endpoint the §10.1 co-existence rule applies: a DPoP
///     proof header is validated against <c>POST {issuer}/Connect/Par</c> and its thumbprint
///     behaves as the supplied <c>dpop_jkt</c>, including the mismatch rejection when both are
///     present.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
public sealed class AdviceAuthorizeDpopJkt<TApp>(
    DPopProofValidator                     proofs,
    IOptions<SchemataAuthorizationOptions> options
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeDpopJkt.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        var jkt = authz.Request?.DpopJkt;

        if (authz.Stage == AuthorizationRequestStage.Pushed && authz.DpopProof is { Length: > 0 } proof) {
            // §10.1: the proof covers the PAR endpoint; the nonce step does not apply here.
            var htu = new Uri(CanonicalIssuer.Combine(options.Value.Issuer, Endpoints.Par)).GetLeftPart(UriPartial.Path);
            var thumbprint = await proofs.ValidateAsync(
                proof,
                "POST",
                new(htu),
                null,
                null,
                authz.Application?.ClientId ?? string.Empty,
                ct);

            if (string.IsNullOrWhiteSpace(jkt)) {
                jkt = thumbprint;
                if (authz.Request is not null) {
                    authz.Request.DpopJkt = thumbprint;
                }
            } else if (!string.Equals(jkt, thumbprint, StringComparison.Ordinal)) {
                throw new OAuthException(OAuthErrors.InvalidDpopProof, SchemataResources.DPOP_JKT_MISMATCH);
            }
        }

        if (string.IsNullOrWhiteSpace(jkt)) {
            return AdviseResult.Continue;
        }

        // §10: the parameter value is an RFC 7638 SHA-256 JWK thumbprint — base64url decoding
        // to exactly 32 bytes. The decoder signals malformed input with FormatException.
        byte[] decoded;
        try {
            decoded = Base64UrlEncoder.DecodeBytes(jkt);
        } catch (FormatException) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.DPOP_JKT_MALFORMED);
        }

        if (decoded.Length != 32) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.DPOP_JKT_MALFORMED);
        }

        return AdviseResult.Continue;
    }

    #endregion
}
