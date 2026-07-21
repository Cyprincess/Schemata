using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
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
    /// <param name="ct">A cancellation token.</param>
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
                    SchemataResources.GetResourceString(SchemataResources.INTERNAL)
                );
        }

        var names = await scopes.ListAsync(ct: ct).Map(s => s.Name!, ct).ToListAsync(ct);
        document.ScopesSupported = [..names];

        return AuthorizationResult.Content(document);
    }

    /// <summary>
    ///     Returns a JSON Web Key Set containing the public key
    ///     material needed to verify tokens issued by this OP.
    ///     Symmetric keys result in an empty <c>keys</c> array because they
    ///     cannot be disclosed publicly,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7517.html#section-4.2">
    ///         RFC 7517: JSON Web Key (JWK) §4.2: "use"
    ///         (Public Key Use) Parameter
    ///     </seealso>
    ///     .
    /// </summary>
    /// <remarks>
    ///     Conversion is public-key-first: a fresh <see cref="SecurityKey" /> holding only public
    ///     parameters is built from the configured key and only that copy is passed to
    ///     <see cref="JsonWebKeyConverter" />, so private material can never reach the wire even if
    ///     converter support is extended to further key types later.
    ///     <see cref="RsaSecurityKey" />, <see cref="ECDsaSecurityKey" />, and
    ///     <see cref="X509SecurityKey" /> are supported; every other key type fails closed with
    ///     <see cref="NotSupportedException" />. A <see cref="JsonWebKey" /> supplied as the signing
    ///     key is rejected the same way, since it may carry private parameters.
    ///     X.509 entries publish only the leaf certificate in <c>x5c</c> and its SHA-256 thumbprint
    ///     under <c>x5t#S256</c>; the SHA-1 <c>x5t</c> member is not emitted,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7517.html#section-4.9">
    ///         RFC 7517: JSON Web Key (JWK) §4.9: "x5t#S256" (X.509 Certificate SHA-256 Thumbprint)
    ///         Parameter
    ///     </seealso>
    ///     .
    /// </remarks>
    /// <exception cref="InvalidOperationException">No signing key is configured.</exception>
    /// <exception cref="NotSupportedException">The configured signing key type is not publishable.</exception>
    public AuthorizationResult GetJwks() {
        var key = options.Value.SigningKey;

        if (key is null) {
            throw new InvalidOperationException(
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Signing key")
            );
        }

        // Symmetric keys MUST NOT appear in a public JWKS.
        // See RFC 7517 §4.2.
        if (key is SymmetricSecurityKey) {
            return AuthorizationResult.Content(new Dictionary<string, object> {
                ["keys"] = Array.Empty<object>(),
            });
        }

        var publicKey = key switch {
            RsaSecurityKey rsa     => CreatePublicKey(rsa),
            ECDsaSecurityKey ecdsa => CreatePublicKey(ecdsa),
            X509SecurityKey x509   => CreatePublicKey(x509),
            _ => throw new NotSupportedException(
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), key.GetType().Name)
            ),
        };

        var jwk = JsonWebKeyConverter.ConvertFromSecurityKey(publicKey);
        jwk.Kid = key.KeyId;
        jwk.Use = "sig";
        jwk.Alg = options.Value.SigningAlgorithm!;

        var entry = new JwkEntry {
            Kty = jwk.Kty,
            Use = jwk.Use,
            Alg = jwk.Alg,
            Kid = jwk.Kid,
            N   = jwk.N,
            E   = jwk.E,
            Crv = jwk.Crv,
            X   = jwk.X,
            Y   = jwk.Y,
        };

        if (key is X509SecurityKey x509Key) {
            var der = x509Key.Certificate.RawData;
            entry.X5c     = [Convert.ToBase64String(der)];
            entry.X5tS256 = Base64UrlEncoder.Encode(SHA256.HashData(der));
        }

        return AuthorizationResult.Content(new Dictionary<string, object> {
            ["keys"] = new[] { entry },
        });
    }

    private static RsaSecurityKey CreatePublicKey(RsaSecurityKey key) {
        var parameters = key.Rsa is { } rsa
            ? rsa.ExportParameters(false)
            : new RSAParameters { Modulus = key.Parameters.Modulus, Exponent = key.Parameters.Exponent };

        return new(parameters);
    }

    private static ECDsaSecurityKey CreatePublicKey(ECDsaSecurityKey key) {
        return new(ECDsa.Create(key.ECDsa.ExportParameters(false)));
    }

    private static SecurityKey CreatePublicKey(X509SecurityKey key) {
        var certificate = key.Certificate;

        if (certificate.GetRSAPublicKey() is { } rsa) {
            return new RsaSecurityKey(rsa.ExportParameters(false));
        }

        if (certificate.GetECDsaPublicKey() is { } ecdsa) {
            return new ECDsaSecurityKey(ECDsa.Create(ecdsa.ExportParameters(false)));
        }

        throw new NotSupportedException(
            string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), "Certificate public key algorithm")
        );
    }

    /// <summary>
    ///     Wire DTO for a single JWKS entry. <see cref="JsonPropertyNameAttribute" /> pins the
    ///     RFC 7517 member names against any configured naming policy, and the
    ///     <see cref="JsonIgnoreAttribute" /> conditions pin member presence: metadata members
    ///     always serialize (nulls included), while key-material and certificate members serialize
    ///     only when populated.
    /// </summary>
    private sealed class JwkEntry
    {
        [JsonPropertyName(JsonWebKeyParameterNames.Kty)]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Kty { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.Use)]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Use { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.Alg)]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Alg { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.Kid)]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Kid { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.N)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? N { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.E)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? E { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.Crv)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Crv { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.X)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? X { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.Y)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Y { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.X5c)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? X5c { get; set; }

        [JsonPropertyName(JsonWebKeyParameterNames.X5tS256)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? X5tS256 { get; set; }
    }
}
