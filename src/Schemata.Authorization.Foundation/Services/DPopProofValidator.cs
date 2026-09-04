using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Caching.Skeleton;
using static Schemata.Abstractions.SchemataConstants;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Validates a DPoP proof JWT and returns the verified public key's RFC 7638
///     thumbprint, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-4.3">
///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
///         (DPoP) §4.3: Checking DPoP Proofs
///     </seealso>
///     . Rejects with <see cref="OAuthErrors.InvalidDpopProof" /> otherwise; the
///     §4.3 step 10 nonce check rejects with <see cref="OAuthErrors.UseDpopNonce" />
///     so the caller can attach a fresh <see cref="Headers.DpopNonce" /> response header.
/// </summary>
/// <remarks>
///     §4.3 step 1 (at most one DPoP header field) and the second half of step 12
///     (matching the proof key against the key bound to the access token) belong to
///     the caller: it owns header parsing and reads the returned thumbprint against
///     the token's <c>cnf.jkt</c> member.
/// </remarks>
public sealed class DPopProofValidator(
    ICacheProvider                         cache,
    [FromKeyedServices(SecurityConstants.TokenTypes.Nonce)] ITokenStore<SchemataToken> nonces,
    IOptions<DPopOptions>                  options,
    TimeProvider?                          time = null
)
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    ///     Validates the DPoP proof for an incoming request and returns the RFC 7638
    ///     SHA-256 thumbprint of the verified public key.
    /// </summary>
    /// <param name="proof">Raw DPoP HTTP header value.</param>
    /// <param name="htm">HTTP method of the current request (§4.3 step 8).</param>
    /// <param name="htu">HTTP target URI of the current request; query and fragment are normalized away before comparison (§4.3 step 9).</param>
    /// <param name="accessToken">Access token presented with the request; <see langword="null" /> at the token endpoint. Present values enable the §4.3 step 12 <c>ath</c> check.</param>
    /// <param name="nonceProvider">DPoP nonce store provider (<c>dpop</c> at the authorization server, <c>dpop-rs</c> at a resource server); <see langword="null" /> skips the §4.3 step 10 nonce check.</param>
    /// <param name="nonceName">Client or application identifier naming the nonce slot.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="OAuthException">With <see cref="OAuthErrors.InvalidDpopProof" />, or <see cref="OAuthErrors.UseDpopNonce" /> for the nonce step.</exception>
    public async Task<string> ValidateAsync(
        string            proof,
        string            htm,
        Uri               htu,
        string?           accessToken,
        string?           nonceProvider,
        string?           nonceName,
        CancellationToken ct = default
    ) {
        // §4.3 step 2: a single and well-formed JWT.
        JsonWebToken token;
        try {
            token = Handler.ReadJsonWebToken(proof);
        } catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_MALFORMED));
        }

        // §4.3 step 3: all required claims per §4.2.
        if (!token.TryGetPayloadValue<string>("jti", out var jti) || string.IsNullOrEmpty(jti)
         || !token.TryGetPayloadValue<string>("htm", out var proofHtm) || string.IsNullOrEmpty(proofHtm)
         || !token.TryGetPayloadValue<string>("htu", out var proofHtu) || string.IsNullOrEmpty(proofHtu)
         || !token.TryGetPayloadValue<long>("iat", out var iat)) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_CLAIMS_REQUIRED));
        }

        // §4.3 step 4: typ explicitly types the proof per RFC 8725 §3.11.
        if (!string.Equals(token.Typ, TokenMediaTypes.DpopJwt, StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_TYPE_MISMATCH));
        }

        // §4.3 step 5: registered asymmetric algorithm, supported and allowed locally;
        // the allow-list excludes none and symmetric (MAC) algorithms by construction.
        var algorithm = token.Alg;
        if (algorithm is not { Length: > 0 } || !options.Value.SigningAlgorithms.Contains(algorithm)) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_ALGORITHM_REJECTED));
        }

        // §4.3 step 7: the key comes from the proof itself and must be public.
        if (!token.TryGetHeaderValue<string>("jwk", out var jwkJson) || string.IsNullOrEmpty(jwkJson)) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_JWK_REQUIRED));
        }

        JsonWebKey jwk;
        try {
            jwk = new(jwkJson);
        } catch (Exception ex) when (ex is ArgumentException or JsonException) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_JWK_REQUIRED));
        }

        if (HasPrivateMembers(jwk)) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_PRIVATE_KEY));
        }

        // §4.3 step 6: the signature must verify with the public key from the jwk.
        var key = VerificationKey(jwk);
        if (key is null
         || !(await Handler.ValidateTokenAsync(proof, new() {
                ValidAlgorithms       = [algorithm],
                IssuerSigningKey      = key,
                ValidateIssuer        = false,
                ValidateAudience      = false,
                ValidateLifetime      = false,
                RequireSignedTokens   = true,
                RequireExpirationTime = false,
                ClockSkew             = TimeSpan.Zero,
            })).IsValid) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_SIGNATURE_INVALID));
        }

        // §4.3 step 8.
        if (!string.Equals(proofHtm, htm, StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_HTM_MISMATCH));
        }

        // §4.3 step 9: htu comparison ignores query and fragment.
        if (!string.Equals(proofHtu, NormalizeHtu(htu), StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_HTU_MISMATCH));
        }

        // §4.3 step 11: creation time within the acceptable window.
        var now     = _time.GetUtcNow();
        var created = DateTimeOffset.FromUnixTimeSeconds(iat);
        var window  = options.Value.ProofTimeWindow;
        if (created < now - window || created > now + window) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_TIME_WINDOW));
        }

        // §4.3 step 12 (first half): ath binds the proof to the presented access token.
        if (accessToken is not null) {
            var expected = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
            if (!token.TryGetPayloadValue<string>("ath", out var ath) || !string.Equals(ath, expected, StringComparison.Ordinal)) {
                throw new OAuthException(
                    OAuthErrors.InvalidDpopProof,
                    SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_ATH_MISMATCH));
            }
        }

        // §4.3 step 10 (§8): a required nonce must match the current server value.
        if (nonceProvider is not null) {
            var stored = await nonces.GetOrCreateAsync(
                null, nonceProvider, nonceName!, null, options.Value.NonceLifetime, ct);
            if (!token.TryGetPayloadValue<string>("nonce", out var nonce) || !string.Equals(nonce, stored.Value, StringComparison.Ordinal)) {
                throw new OAuthException(
                    OAuthErrors.UseDpopNonce,
                    SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_NONCE_MISMATCH));
            }
        }

        // The jti stays cached exactly as long as the proof remains within the window
        // (§11.1), so a replay is caught by this check before the iat window could
        // admit it again.
        var lifetime = created + window - now;
        if (lifetime < TimeSpan.Zero) {
            lifetime = TimeSpan.Zero;
        }

        var added = await cache.TryAddAsync(
            $"dpop-jti\x1e{jti}".ToCacheKey(Keys.Authorization),
            Encoding.UTF8.GetBytes(jti),
            new() { AbsoluteExpirationRelativeToNow = lifetime },
            ct);

        if (!added) {
            throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_REPLAYED));
        }

        return ComputeThumbprint(jwk)
            ?? throw new OAuthException(
                OAuthErrors.InvalidDpopProof,
                SchemataResources.GetResourceString(SchemataResources.DPOP_PROOF_SIGNATURE_INVALID));
    }

    /// <summary>
    ///     Computes the RFC 7638 SHA-256 thumbprint of a public JWK: the required
    ///     members only, in lexicographic order, without whitespace, hashed as UTF-8
    ///     and base64url-encoded. Returns <see langword="null" /> when the key type is
    ///     unsupported or required members are missing.
    /// </summary>
    public static string? ComputeThumbprint(JsonWebKey jwk) {
        var canonical = jwk.Kty switch {
            "RSA" when jwk.E is { Length: > 0 } && jwk.N is { Length: > 0 }
                => $$"""{"e":"{{jwk.E}}","kty":"{{jwk.Kty}}","n":"{{jwk.N}}"}""",
            "EC" when jwk.Crv is { Length: > 0 } && jwk.X is { Length: > 0 } && jwk.Y is { Length: > 0 }
                => $$"""{"crv":"{{jwk.Crv}}","kty":"{{jwk.Kty}}","x":"{{jwk.X}}","y":"{{jwk.Y}}"}""",
            "OKP" when jwk.Crv is { Length: > 0 } && jwk.X is { Length: > 0 }
                => $$"""{"crv":"{{jwk.Crv}}","kty":"{{jwk.Kty}}","x":"{{jwk.X}}"}""",
            _ => null,
        };

        return canonical is null
            ? null
            : Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    /// <summary>
    ///     Reads the RFC 7638 thumbprint from a token principal's <c>cnf.jkt</c> confirmation
    ///     member
    ///     (<seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-6.1">
    ///         RFC 9449 §6.1: JWK Thumbprint Confirmation
    ///     </seealso>
    ///     ); returns <see langword="null" /> when the claim is absent or carries no jkt object.
    /// </summary>
    internal static string? ReadBoundThumbprint(ClaimsPrincipal principal) {
        var json = principal.FindFirstValue(Claims.Cnf);
        if (string.IsNullOrWhiteSpace(json)) {
            return null;
        }

        try {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(Claims.Jkt, out var jkt)
                && jkt.ValueKind == JsonValueKind.String
                    ? jkt.GetString()
                    : null;
        } catch (JsonException) {
            // A cnf claim that is not a JSON object carries no jkt binding.
            return null;
        }
    }


    /// <summary>Returns the request URI without query and fragment, with RFC 3986 case and default-port normalization applied.</summary>
    internal static string NormalizeHtu(Uri htu) {
        var builder = new UriBuilder(htu) { Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.ToString();
    }

    private static bool HasPrivateMembers(JsonWebKey jwk) {
        return jwk.D is not null || jwk.P is not null || jwk.Q is not null
            || jwk.DP is not null || jwk.DQ is not null || jwk.QI is not null
            || jwk.K is not null || jwk.Oth is { Count: > 0 };
    }

    private static SecurityKey? VerificationKey(JsonWebKey jwk) {
        try {
            return jwk.Kty switch {
                "RSA" => new RsaSecurityKey(new RSAParameters {
                    Modulus  = Base64UrlEncoder.DecodeBytes(jwk.N),
                    Exponent = Base64UrlEncoder.DecodeBytes(jwk.E),
                }),
                "EC" => EcVerificationKey(jwk),
                _ => null,
            };
        } catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException or PlatformNotSupportedException) {
            return null;
        }
    }

    private static SecurityKey? EcVerificationKey(JsonWebKey jwk) {
        ECCurve? curve = jwk.Crv switch {
            "P-256" => ECCurve.NamedCurves.nistP256,
            "P-384" => ECCurve.NamedCurves.nistP384,
            "P-521" => ECCurve.NamedCurves.nistP521,
            _ => null,
        };
        if (curve is null) {
            return null;
        }

        var parameters = new ECParameters {
            Curve = curve.Value,
            Q = new() {
                X = Base64UrlEncoder.DecodeBytes(jwk.X),
                Y = Base64UrlEncoder.DecodeBytes(jwk.Y),
            },
        };

        return new ECDsaSecurityKey(ECDsa.Create(parameters));
    }
}
