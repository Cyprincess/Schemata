using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceRequestDpop{TApp}" />.</summary>
public static class AdviceRequestDpop
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceRequestEndpointPermission.DefaultOrder + 1_000;
}

/// <summary>
///     Token request advisor validating the DPoP proof header and recording the proof key
///     binding for the issued access token, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-5">
///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
///         (DPoP) §5: DPoP Access Token Request
///     </seealso>
///     . Requests without a proof pass through as Bearer unless the client registered
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-5.2">
///         §5.2: Client Registration Metadata
///     </seealso>
///     <c>dpop_bound_access_tokens</c> or the host enabled the
///     <see cref="DPopOptions.RequireForAllClients" /> override; a missing or mismatched
///     proof nonce is answered with a <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-8">
///         §8: Authorization Server-Provided Nonce
///     </seealso>
///     challenge carrying the current value in a <c>DPoP-Nonce</c> response header.
/// </summary>
/// <seealso cref="DPopProofValidator" />
/// <seealso cref="DpopBinding" />
public sealed class AdviceRequestDpop<TApp>(
    DPopProofValidator                     proofs,
    [FromKeyedServices(SecurityConstants.TokenTypes.Nonce)] ITokenStore<SchemataToken> nonces,
    IOptions<SchemataAuthorizationOptions> options,
    IOptions<DPopOptions>                  dpop
) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ITokenRequestAdvisor<TApp> Members

    public int Order => AdviceRequestDpop.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        TokenRequest      request,
        CancellationToken ct = default
    ) {
        var clientId = application.ClientId;
        if (string.IsNullOrWhiteSpace(clientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.ClientId)
            );
        }

        var proof = ctx.TryGet<DpopProof>(out var carrier) ? carrier?.Value : null;

        if (string.IsNullOrWhiteSpace(proof)) {
            // §5.2: a client registered with dpop_bound_access_tokens — or, under the
            // RequireForAllClients override, every client — must present a DPoP header
            // on every token request.
            if (application.DpopBoundAccessTokens || dpop.Value.RequireAllClients) {
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_REQUIRED));
            }

            return AdviseResult.Continue;
        }

        // §5: the proof covers the token endpoint URI; query and fragment are normalized
        // away by the validator's htu comparison.
        var htu = new Uri($"{options.Value.Issuer}{Endpoints.Token}").GetLeftPart(UriPartial.Path);

        var key = NonceProvider;
        string jkt;
        try {
            jkt = await proofs.ValidateAsync(proof, "POST", new(htu), null, key, clientId, ct);
        } catch (OAuthException ex) when (ex.Status == OAuthErrors.UseDpopNonce) {
            // §8: the validator's nonce step rejected the proof; answer with HTTP 400
            // use_dpop_nonce and the stored value in a DPoP-Nonce response header.
            var nonce = (await nonces.GetOrCreateAsync(
                null, key, clientId, null, dpop.Value.NonceLifetime, ct)).Value
                ?? throw new InvalidOperationException("The DPoP nonce store returned an empty nonce value.");
            ex.Headers ??= new Dictionary<string, string>();
            ex.Headers[Headers.DpopNonce] = nonce;
            throw;
        }

        ctx.Set(new DpopBinding(jkt));

        return AdviseResult.Continue;
    }

    private const string NonceProvider = "dpop";

    #endregion
}
