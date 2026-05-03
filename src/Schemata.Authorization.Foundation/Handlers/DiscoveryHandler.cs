using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     OIDC Discovery endpoint per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderConfig">
///         OpenID Connect Discovery 1.0
///         §4: Obtaining OpenID Provider Configuration Information
///     </seealso>
///     .
///     Builds the OP's discovery document from <see cref="SchemataAuthorizationOptions" />
///     and publishes the JSON Web Key Set for signature verification per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7517.html">RFC 7517: JSON Web Key (JWK)</seealso>.
///     Symmetric keys are excluded from the public JWKS.
/// </summary>
public sealed class DiscoveryHandler<TScope>(
    IOptions<SchemataAuthorizationOptions> options,
    IScopeManager<TScope>                  scopes,
    IServiceProvider                       sp
)
    where TScope : SchemataScope
{
    /// <summary>
    ///     Returns the OIDC discovery document containing server metadata:
    ///     supported response types, response modes, grant types, subject types,
    ///     signing algorithms, claims, and the JWKS endpoint URI.
    ///     Runs the <see cref="IDiscoveryAdvisor" /> pipeline for extensibility.
    /// </summary>
    /// <param name="issuer">The issuer URI for this OP instance.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AuthorizationResult> GetDiscoveryDocumentAsync(string issuer, CancellationToken ct) {
        var config = options.Value;

        var document = new DiscoveryDocument {
            Issuer                 = issuer,
            ResponseTypesSupported = [..config.AllowedResponseTypes],
            ResponseModesSupported = [..config.AllowedResponseModes],
            SubjectTypesSupported = string.IsNullOrWhiteSpace(config.PairwiseSalt)
                ? [SubjectTypes.Public]
                : [SubjectTypes.Public, SubjectTypes.Pairwise],
            IdTokenSigningAlgValuesSupported           = [config.SigningAlgorithm!],
            ClaimsSupported                            = [..config.SupportedClaims],
            TokenEndpointAuthMethodsSupported          = [..config.AllowedClientAuthMethods],
            AuthorizationResponseIssParameterSupported = !string.IsNullOrWhiteSpace(issuer),
        };

        var ctx = new AdviceContext(sp);
        var discovery = new DiscoveryContext {
            Issuer                           = issuer,
            Document                         = document,
            SupportsAuthorizationResponseIss = !string.IsNullOrWhiteSpace(issuer),
        };

        switch (await Advisor.For<IDiscoveryAdvisor>()
                             .RunAsync(ctx, discovery, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return AuthorizationResult.Content(discovery.Document);
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.ServerError,
                    SchemataResources.GetResourceString(SchemataResources.ST4014)
                );
        }

        var names = await scopes.ListAsync(ct: ct).Map(s => s.Name!, ct).ToListAsync(ct);
        document.ScopesSupported = [..names];

        return AuthorizationResult.Content(document);
    }

    /// <summary>
    ///     Returns a JSON Web Key Set containing the public key
    ///     material needed to verify tokens issued by this OP.
    ///     Private key components are stripped before publication.
    ///     Symmetric keys result in an empty <c>keys</c> array because they
    ///     cannot be disclosed publicly,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7517.html#section-4.2">
    ///         RFC 7517: JSON Web Key (JWK) §4.2: "use"
    ///         (Public Key Use) Parameter
    ///     </seealso>
    ///     .
    /// </summary>
    public AuthorizationResult GetJwks() {
        if (options.Value.SigningKey == null) {
            throw new InvalidOperationException(
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1016), "Signing key")
            );
        }

        // symmetric keys MUST NOT appear in a public JWKS.
        // See RFC 7517 §4.2.
        if (options.Value.SigningKey is SymmetricSecurityKey) {
            return AuthorizationResult.Content(new Dictionary<string, object> {
                ["keys"] = Array.Empty<object>(),
            });
        }

        var jwk = JsonWebKeyConverter.ConvertFromSecurityKey(options.Value.SigningKey);
        jwk.Use = "sig";
        jwk.Alg = options.Value.SigningAlgorithm!;

        // Strip private key material
        jwk.D  = null;
        jwk.P  = null;
        jwk.Q  = null;
        jwk.DP = null;
        jwk.DQ = null;
        jwk.QI = null;
        jwk.K  = null;

        var entry = new Dictionary<string, string?> {
            [JsonWebKeyParameterNames.Kty] = jwk.Kty,
            [JsonWebKeyParameterNames.Use] = jwk.Use,
            [JsonWebKeyParameterNames.Alg] = jwk.Alg,
            [JsonWebKeyParameterNames.Kid] = jwk.Kid,
        };

        switch (options.Value.SigningKey) {
            case RsaSecurityKey:
                entry[JsonWebKeyParameterNames.N] = jwk.N;
                entry[JsonWebKeyParameterNames.E] = jwk.E;
                break;
            case ECDsaSecurityKey:
                entry[JsonWebKeyParameterNames.Crv] = jwk.Crv;
                entry[JsonWebKeyParameterNames.X]   = jwk.X;
                entry[JsonWebKeyParameterNames.Y]   = jwk.Y;
                break;
        }

        return AuthorizationResult.Content(new Dictionary<string, object> {
            ["keys"] = new[] { entry },
        });
    }
}
