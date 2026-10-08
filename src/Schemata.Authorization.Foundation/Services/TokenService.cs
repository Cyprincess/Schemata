using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Core token creation and validation service.
///     Creates signed JWTs, encrypted JWEs, opaque reference tokens, and OIDC
///     ID tokens.  Signing and encryption keys come from security rows stored under the
///     configured issuer; <see cref="BeginSigningAsync" /> resolves them once per operation
///     into a <see cref="SigningContext" />, so rotation takes effect between operations
///     without restart.  All token claims include <c>iss</c>, <c>iat</c>, <c>exp</c>, and <c>jti</c>.
/// </summary>
public class TokenService(
    ISecurityStore<SchemataSecurity>       securities,
    IHttpClientFactory                     http,
    ICacheProvider                         cache,
    IOptions<SchemataSecurityOptions>      securityOptions,
    IOptions<SchemataAuthorizationOptions> options,
    TimeProvider?                          time = null
)
{
    private readonly JsonWebTokenHandler          _handler = new() { SetDefaultTimesOnTokenCreation = false };
    private readonly SchemataAuthorizationOptions _options = options.Value;
    private readonly TimeProvider                 _time    = time ?? TimeProvider.System;
    private readonly LifetimeValidator _validateLifetime = (notBefore, expires, _, validation) => {
        if (!validation.ValidateLifetime) return true;
        var now = (time ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return expires is { } expiry && (notBefore is null || notBefore <= expiry)
            && (notBefore is null || notBefore <= now + validation.ClockSkew)
            && expiry >= now - validation.ClockSkew;
    };

    // Imported keys are fresh per resolution; caching their signature providers in the
    // process-wide default factory would retain every key the issuer ever imported.
    private static readonly CryptoProviderFactory NoProviderCache = new() { CacheSignatureProviders = false };

    /// <summary>
    ///     Resolves the signing context for one operation: the newest valid signing row is
    ///     primary, a non-blank <paramref name="algorithm" /> (OIDC Registration §2
    ///     <c>id_token_signed_response_alg</c>) selects the newest valid row serving it, valid
    ///     and retired rows verify, and the newest valid encryption row (when present)
    ///     encrypts. Nothing is cached across operations, so rotation stays observable.
    /// </summary>
    /// <param name="algorithm">Negotiated JWS signing algorithm; blank selects the primary row.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">No valid signing row exists, a selected row
    /// carries no key material or algorithm, the negotiated algorithm has no serving row, or a
    /// multi-row verification set carries a blank key id.</exception>
    /// <exception cref="NotSupportedException">A private-key row fails its algorithm-driven
    /// key import; see <see cref="SecurityKeyMaterialExtensions.ToKeyMaterialAsync" />.</exception>
    public async ValueTask<SigningContext> BeginSigningAsync(string? algorithm = null, CancellationToken ct = default) {
        return await ResolveContextAsync(algorithm, ct);
    }

    private async Task<SigningContext> ResolveContextAsync(string? algorithm, CancellationToken ct) {
        var owned = new List<SecurityKey>();
        try {
            var signingRows = await ListRowsAsync(SecurityConstants.Usages.Signing, ct);
            var primary = signingRows.FirstOrDefault(
                              row => row.Status == SecurityConstants.Statuses.Valid)
                          ?? throw new InvalidOperationException(
                              string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Signing key"));

            // With multiple verification keys the kid header is the only way to route a
            // signature to its key, so every key in a set must carry a key id. A single
            // bare key remains valid: RFC 7517 §4.5 makes kid a SHOULD, not a MUST.
            var trusted = signingRows
                .Where(row => row.Status is SecurityConstants.Statuses.Valid or SecurityConstants.Statuses.Retired)
                .ToList();
            if (trusted.Count > 1 && trusted.Any(row => string.IsNullOrEmpty(row.Kid))) {
                throw new InvalidOperationException(
                    string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), "Key id"));
            }

            var primaryKey = Track(
                await ToKeyAsync(primary, ct) ?? throw new InvalidOperationException(
                    string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Signing key")),
                owned);
            var primaryCredentials = CreateSigningCredentials(primary, primaryKey);

            SigningCredentials signing = primaryCredentials;
            (SchemataSecurity Row, SecurityKey Key)? selection = null;
            if (!string.IsNullOrWhiteSpace(algorithm)
             && !string.Equals(primaryCredentials.Algorithm, algorithm, StringComparison.Ordinal)) {
                // A negotiated algorithm the issuer cannot serve fails explicitly instead of
                // silently signing with a different one.
                var selected = signingRows.FirstOrDefault(
                                   row => row.Status == SecurityConstants.Statuses.Valid
                                       && string.Equals(SecurityKeyAdapter.ToSigningAlgorithm(row.Algorithm ?? string.Empty),
                                                        algorithm, StringComparison.Ordinal))
                               ?? throw new InvalidOperationException(
                                   string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), $"Signing key for {algorithm}"));
                var selectedKey = Track(
                    await ToKeyAsync(selected, ct) ?? throw new InvalidOperationException(
                        string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Signing key")),
                    owned);
                signing = CreateSigningCredentials(selected, selectedKey);
                selection = (selected, selectedKey);
            }

            var keys = new List<SecurityKey>();
            foreach (var row in trusted) {
                SecurityKey key;
                if (ReferenceEquals(row, primary)) {
                    key = primaryKey;
                } else if (selection is { } chosen && ReferenceEquals(row, chosen.Row)) {
                    key = chosen.Key;
                } else if (await ToKeyAsync(row, ct) is { } imported) {
                    key = Track(imported, owned);
                } else {
                    continue;
                }

                keys.Add(key);
            }

            EncryptingCredentials? encrypting = null;
            var decryption = new List<SecurityKey>();
            foreach (var row in await ListRowsAsync(SecurityConstants.Usages.Encryption, ct)) {
                if (row.Status is not (SecurityConstants.Statuses.Valid or SecurityConstants.Statuses.Retired)) {
                    continue;
                }
                if (await ToKeyAsync(row, ct) is not { } key) {
                    throw new InvalidOperationException(
                        string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Encryption key"));
                }
                Track(key, owned);
                decryption.Add(key);
                if (encrypting is null && row.Status == SecurityConstants.Statuses.Valid) {
                    var algorithmName = row.Algorithm ?? throw new InvalidOperationException(
                        string.Format(SchemataResources.GetResourceString(SchemataResources.MISSING_DEPENDENT_SETTING),
                                      "Encryption key", "Encryption algorithm"));
                    encrypting = new(key, SecurityKeyAdapter.ToEncryptionAlgorithm(algorithmName), _options.ContentEncryptionAlgorithm) {
                        CryptoProviderFactory = NoProviderCache,
                    };
                }
            }

            var validation = new TokenValidationParameters {
                ValidIssuer        = _options.Issuer,
                ValidateAudience   = false,
                IssuerSigningKeys  = keys,
                TokenDecryptionKeys = decryption,
                ClockSkew          = _options.TokenValidationClockSkew,
            };

            return new(signing, primaryCredentials, encrypting, validation, owned);
        } catch {
            SigningContext.DisposeKeys(owned);
            throw;
        }
    }

    private static SigningCredentials CreateSigningCredentials(SchemataSecurity row, SecurityKey key) {
        var keyAlgorithm = row.Algorithm ?? throw new InvalidOperationException(
            string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Signing algorithm"));
        return new(key, SecurityKeyAdapter.ToSigningAlgorithm(keyAlgorithm)) {
            CryptoProviderFactory = NoProviderCache,
        };
    }

    internal async Task<List<string>> GetSigningAlgorithmsAsync(CancellationToken ct) {
        var algorithms = new List<string>();
        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Issuer(_options.Issuer!), null, SecurityConstants.Usages.Signing, null, ct)) {
            if (row.Status != SecurityConstants.Statuses.Valid) continue;
            var key = await ToKeyAsync(row, ct) ?? throw new InvalidOperationException(
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Signing key"));
            try {
                var credentials = CreateSigningCredentials(row, key);
                var signer = NoProviderCache.CreateForSigning(key, credentials.Algorithm);
                NoProviderCache.ReleaseSignatureProvider(signer);
                if (!algorithms.Contains(credentials.Algorithm)) algorithms.Add(credentials.Algorithm);
            } finally {
                if (key is RsaSecurityKey { Rsa: { } rsa }) rsa.Dispose();
                else if (key is ECDsaSecurityKey { ECDsa: { } ec }) ec.Dispose();
            }
        }
        return algorithms;
    }

    private static SecurityKey Track(SecurityKey key, List<SecurityKey> owned) {
        key.CryptoProviderFactory = NoProviderCache;
        owned.Add(key);
        return key;
    }

    /// <summary>
    ///     Computes the OIDC left-half base64url hash of <paramref name="value" /> with the algorithm
    ///     of <paramref name="signing" />, identical to the <c>at_hash</c> / <c>c_hash</c> math at OIDC Core §3.1.3.8.
    /// </summary>
    public static string ComputeHash(string value, SigningCredentials signing) {
        return ComputeHash(value, signing.Algorithm);
    }

    /// <summary>
    ///     Computes the OIDC left-half base64url hash using the named JWS signing algorithm.
    /// </summary>
    /// <param name="value">ASCII token value to hash.</param>
    /// <param name="algorithm">JWS signing algorithm whose hash size determines the digest.</param>
    /// <returns>The base64url-encoded left half of the digest.</returns>
    public static string ComputeHash(string value, string algorithm) {
        var       bytes  = Encoding.ASCII.GetBytes(value);
        using var hash   = CryptoProviderFactory.Default.CreateHashAlgorithm(GetHashAlgorithm(algorithm));
        var       hashed = hash.ComputeHash(bytes);
        return Base64UrlEncoder.Encode(hashed, 0, hashed.Length / 2);
    }

    private async Task<List<SchemataSecurity>> ListRowsAsync(string usage, CancellationToken ct) {
        var rows = new List<SchemataSecurity>();
        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Issuer(_options.Issuer!), null, usage, null, ct)) {
            rows.Add(row);
        }

        return rows;
    }

    private async Task<SecurityKey?> ToKeyAsync(SchemataSecurity row, CancellationToken ct) {
        var material = await row.ToKeyMaterialAsync(
            http.CreateClient(SecurityKeyMaterialExtensions.HttpClientName),
            cache,
            securityOptions.Value.KeyCacheLifetime,
            ct);

        return material is null ? null : SecurityKeyAdapter.ToSecurityKey(material);
    }

    /// <summary>
    ///     Creates a signed JWT or encrypted JWE with caller-selected absolute issue and expiry
    ///     timestamps, signed with <paramref name="credentials" /> drawn from the operation's
    ///     <paramref name="context" />. Credential resolution cannot move either boundary.
    /// </summary>
    public string CreateToken(
        SigningContext     context,
        SigningCredentials credentials,
        IEnumerable<Claim> claims,
        DateTimeOffset     issuedAt,
        DateTimeOffset     expiresAt,
        bool               encrypt = false,
        string?            typ = null
    ) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(credentials);
        if (_time.GetUtcNow() >= expiresAt) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        if (encrypt && context.Encrypting is null) {
            throw new InvalidOperationException(
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Encryption key"));
        }

        var descriptor = new SecurityTokenDescriptor {
            Subject                = new(claims),
            Expires                = expiresAt.UtcDateTime,
            IssuedAt               = issuedAt.UtcDateTime,
            Issuer                 = _options.Issuer,
            TokenType              = typ,
            AdditionalHeaderClaims = encrypt ? new Dictionary<string, object> { ["cty"] = TokenMediaTypes.NestedJwt } : null,
            SigningCredentials     = credentials,
            EncryptingCredentials  = encrypt ? context.Encrypting : null,
        };
        return _handler.CreateToken(descriptor);
    }

    /// <summary>Generates a cryptographically random opaque reference string (Base64URL-encoded).</summary>
    public string CreateReference() { return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)); }

    /// <summary>
    ///     Creates an OIDC ID token with caller-selected absolute timestamps and optional
    ///     <c>at_hash</c>, <c>c_hash</c>, and <c>nonce</c> claims. The JOSE signature and both
    ///     hash claims derive from the context's negotiated selection.
    /// </summary>
    public string CreateIdToken(
        SigningContext context,
        List<Claim>    claims,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string?        at    = null,
        string?        code  = null,
        string?        nonce = null
    ) {
        var signing = context.Signing;

        claims.Add(new(Claims.TokenUse, "id_token"));

        if (!string.IsNullOrWhiteSpace(nonce)) {
            claims.Add(new(Claims.Nonce, nonce));
        }

        if (!string.IsNullOrWhiteSpace(at)) {
            claims.Add(new(Claims.AtHash, ComputeHash(at, signing.Algorithm)));
        }

        if (!string.IsNullOrWhiteSpace(code)) {
            claims.Add(new(Claims.CHash, ComputeHash(code, signing.Algorithm)));
        }

        return CreateToken(context, signing, claims, issuedAt, expiresAt);
    }

    /// <summary>
    ///     Validates a JWT or JWE token string against the configured issuer
    ///     and the issuer's signing rows. When <paramref name="audience" /> is provided,
    ///     audience validation is enforced.
    /// </summary>
    /// <param name="token">The JWT/JWE token string, or stored payload for reference tokens.</param>
    /// <param name="audience">Expected application canonical name; null disables audience validation.</param>
    /// <param name="lifetime">When <c>false</c>, expired tokens are still accepted (used for refresh token inspection).</param>
    public async Task<ClaimsPrincipal?> Validate(string? token, string? audience = null, bool lifetime = true) {
        return (await ValidateTokenAsync(token, audience, lifetime)).Principal;
    }

    internal async Task<(ClaimsPrincipal? Principal, DateTimeOffset? Expires)> ValidateForLogout(
        string? token, string? audience = null) {
        var result = await ValidateTokenAsync(token, audience, lifetime: false);
        return (result.Principal, result.ValidTo is null or { Ticks: 0 }
            ? null
            : new DateTimeOffset(result.ValidTo.Value, TimeSpan.Zero));
    }

    /// <summary>
    ///     Validates a presented self-contained access token: signature, issuer, audience, and
    ///     lifetime run through the cryptographic validation, and the authenticated profile is
    ///     read from the cryptographically verified token — for a nested encrypted token the
    ///     inner signed JWT, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9068.html#section-5">
    ///         RFC 9068: Profile for OAuth 2.0 Access Tokens §5: Authorization Header
    ///     </seealso>
    ///     . The inner <c>typ</c> must be the access-token media type (<c>at+jwt</c> or the
    ///     equivalent long <c>application/at+jwt</c> form); the outer header alone neither
    ///     rescues a wrong inner type nor rejects a correct one.
    /// </summary>
    /// <param name="token">The JWT or JWE access token to validate.</param>
    /// <param name="audience">Expected application canonical name; null disables audience validation.</param>
    public async Task<ClaimsPrincipal?> ValidateAccessToken(string? token, string? audience = null) {
        var (principal, profileTyp, _) = await ValidateTokenAsync(token, audience, lifetime: true);
        if (principal is null) {
            return null;
        }

        return IsAccessTokenMediaType(profileTyp) ? principal : null;
    }

    private async Task<(ClaimsPrincipal? Principal, string? ProfileTyp, DateTime? ValidTo)> ValidateTokenAsync(
        string? token,
        string? audience,
        bool    lifetime
    ) {
        if (string.IsNullOrWhiteSpace(token)) {
            return (null, null, null);
        }

        await using var context = await ResolveContextAsync(null, CancellationToken.None);
        var parameters = context.Validation.Clone();

        parameters.ValidateLifetime = lifetime;
        parameters.LifetimeValidator = _validateLifetime;

        if (!string.IsNullOrWhiteSpace(audience)) {
            parameters.ValidAudience    = audience;
            parameters.ValidateAudience = true;
        }

        var result = await _handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid) {
            return (null, null, null);
        }

        // The resolved keys are disposed when this method returns; the verified token retains
        // them, so callers receive a detached identity and the only token metadata they consume
        // (profile typ, expiry). The detached identity stays a CaseSensitiveClaimsIdentity to
        // preserve the handler's exact-case claim lookup.
        var verified = result.SecurityToken as JsonWebToken;
        var identity = new CaseSensitiveClaimsIdentity(
            result.ClaimsIdentity.Claims,
            result.ClaimsIdentity.AuthenticationType,
            result.ClaimsIdentity.NameClaimType,
            result.ClaimsIdentity.RoleClaimType);

        var profile = verified?.InnerToken ?? verified;
        // IdentityModel emits no claim for an empty JSON array; restore the verified empty
        // grant so an explicit `[]` actual set survives into the next refresh (RFC 9396 §6.1).
        if (profile is not null
         && profile.TryGetPayloadValue(Claims.AuthorizationDetails, out JsonElement details)
         && details.ValueKind == JsonValueKind.Array
         && details.GetArrayLength() == 0
         && !identity.HasClaim(c => c.Type == Claims.AuthorizationDetails)) {
            identity.AddClaim(new(Claims.AuthorizationDetails, "[]", JsonClaimValueTypes.Json));
        }

        return (new(identity), profile?.Typ, profile?.ValidTo);
    }

    private static bool IsAccessTokenMediaType(string? typ) {
        return string.Equals(typ, TokenMediaTypes.AccessToken, StringComparison.OrdinalIgnoreCase)
            || string.Equals(typ, $"application/{TokenMediaTypes.AccessToken}", StringComparison.OrdinalIgnoreCase);
    }


    private static string GetHashAlgorithm(string algorithm) {
        return algorithm switch {
            SigningAlgorithms.RsaSha256 or SigningAlgorithms.EcdsaSha256 or SigningAlgorithms.RsaPssSha256 or SigningAlgorithms.HmacSha256 => "SHA256",
            SigningAlgorithms.RsaSha384 or SigningAlgorithms.EcdsaSha384 or SigningAlgorithms.RsaPssSha384 or SigningAlgorithms.HmacSha384 => "SHA384",
            SigningAlgorithms.RsaSha512 or SigningAlgorithms.EcdsaSha512 or SigningAlgorithms.RsaPssSha512 or SigningAlgorithms.HmacSha512 => "SHA512",
            var _ => throw new NotSupportedException(string.Format(SchemataResources.GetResourceString(SchemataResources.UNSUPPORTED_ALGORITHM), algorithm)),
        };
    }
}
