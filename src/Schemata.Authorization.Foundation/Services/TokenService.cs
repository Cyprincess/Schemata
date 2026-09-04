using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
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
///     configured issuer; credentials are resolved per call so rotation takes effect
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

    /// <summary>Builds the credential set from the issuer's security rows: the newest valid
    /// signing row is primary, valid and retired rows verify, and the newest valid
    /// encryption row (when present) encrypts.</summary>
    /// <exception cref="InvalidOperationException">No valid signing row exists, the primary
    /// row carries no key material or algorithm, or a multi-row verification set carries a
    /// blank key id.</exception>
    /// <exception cref="NotSupportedException">A private-key row fails its algorithm-driven
    /// key import; see <see cref="SecurityKeyMaterialExtensions.ToKeyMaterialAsync" />.</exception>
    private async Task<(SigningCredentials Signing, EncryptingCredentials? Encrypting, TokenValidationParameters Validation)>
        ResolveCredentials(CancellationToken ct) {
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

        var keyAlgorithm = primary.Algorithm ?? throw new InvalidOperationException(
            string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Signing algorithm"));
        var algorithm = SecurityKeyAdapter.ToSigningAlgorithm(keyAlgorithm);

        var primaryKey = await ToKeyAsync(primary, ct) ?? throw new InvalidOperationException(
            string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Signing key"));

        var keys = new List<SecurityKey>();
        foreach (var row in trusted) {
            if (await ToKeyAsync(row, ct) is { } key) {
                keys.Add(key);
            }
        }

        var encryptionRow = (await ListRowsAsync(SecurityConstants.Usages.Encryption, ct)).FirstOrDefault(
            row => row.Status == SecurityConstants.Statuses.Valid);

        EncryptingCredentials? encrypting = null;
        SecurityKey?           decryption = null;
        if (encryptionRow is not null) {
            var encryptionKeyAlgorithm = encryptionRow.Algorithm ?? throw new InvalidOperationException(
                string.Format(
                    SchemataResources.GetResourceString(SchemataResources.MISSING_DEPENDENT_SETTING),
                    "Encryption key", "Encryption algorithm"));
            var encryptionAlgorithm = SecurityKeyAdapter.ToEncryptionAlgorithm(encryptionKeyAlgorithm);

            if (await ToKeyAsync(encryptionRow, ct) is { } encryptionKey) {
                encrypting = new(encryptionKey, encryptionAlgorithm, _options.ContentEncryptionAlgorithm);
                decryption = encryptionKey;
            }
        }

        var validation = new TokenValidationParameters {
            ValidIssuer        = _options.Issuer,
            ValidateAudience   = false,
            IssuerSigningKeys  = keys,
            TokenDecryptionKey = decryption,
            ClockSkew          = TimeSpan.FromMinutes(1),
        };

        return (new(primaryKey, algorithm), encrypting, validation);
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
    ///     Creates a signed JWT (or encrypted JWE when <paramref name="encrypt" /> is <c>true</c>).
    ///     Sets <c>iss</c>, <c>iat</c>, <c>exp</c> automatically.
    /// </summary>
    /// <param name="claims">Claims to embed in the token.</param>
    /// <param name="lifetime">Token validity duration.</param>
    /// <param name="encrypt">When <c>true</c>, wraps the JWT as a JWE.</param>
    /// <param name="typ">
    ///     Media type stamped into the <c>typ</c> header; for a nested JWT the library stamps the
    ///     outer <c>cty</c> as <c>JWT</c>, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7519.html#section-5.2">
    ///         RFC 7519: JSON Web Token (JWT) §5.2: "cty" (Content Type) Header Parameter
    ///     </seealso>
    ///     .
    /// </param>
    public async Task<string> CreateToken(IEnumerable<Claim> claims, TimeSpan lifetime, bool encrypt = false, string? typ = null) {
        var (signing, encrypting, _) = await ResolveCredentials(CancellationToken.None);

        if (encrypt && encrypting is null) {
            throw new InvalidOperationException(
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), "Encryption key"));
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor {
            Subject                = new(claims),
            Expires                = now + lifetime,
            IssuedAt               = now,
            Issuer                 = _options.Issuer,
            TokenType              = typ,
            AdditionalHeaderClaims = encrypt ? new Dictionary<string, object> { ["cty"] = TokenMediaTypes.NestedJwt } : null,
            SigningCredentials     = signing,
            EncryptingCredentials  = encrypt ? encrypting : null,
        };
        return _handler.CreateToken(descriptor);
    }

    /// <summary>Generates a cryptographically random opaque reference string (Base64URL-encoded).</summary>
    public string CreateReference() { return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)); }

    /// <summary>
    ///     Creates an OIDC ID token with <c>token_use: id_token</c>, optional
    ///     <c>at_hash</c> and <c>c_hash</c> computed per
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#CodeFlowTokenValidation">
    ///         OpenID Connect Core 1.0
    ///         §3.1.3.8: Access Token Validation
    ///     </seealso>
    ///     ,
    ///     and <c>nonce</c> per
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#IDTokenValidation">
    ///         OpenID Connect Core 1.0
    ///         §3.1.3.7: ID Token Validation
    ///     </seealso>
    ///     .
    /// </summary>
    /// <param name="claims">ID token claims.</param>
    /// <param name="lifetime">Token validity duration.</param>
    /// <param name="at">Access token value for <c>at_hash</c>.</param>
    /// <param name="code">Authorization code for <c>c_hash</c>.</param>
    /// <param name="nonce">Opaque nonce from the authorization request.</param>
    public async Task<string> CreateIdToken(
        List<Claim> claims,
        TimeSpan    lifetime,
        string?     at    = null,
        string?     code  = null,
        string?     nonce = null
    ) {
        var (signing, _, _) = await ResolveCredentials(CancellationToken.None);

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

        return await CreateToken(claims, lifetime);
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
        if (string.IsNullOrWhiteSpace(token)) {
            return null;
        }

        var (_, _, template) = await ResolveCredentials(CancellationToken.None);
        var parameters = template.Clone();

        parameters.ValidateLifetime = lifetime;

        if (!string.IsNullOrWhiteSpace(audience)) {
            parameters.ValidAudience    = audience;
            parameters.ValidateAudience = true;
        }

        var result = await _handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid) {
            return null;
        }

        return new(result.ClaimsIdentity);
    }

    // at_hash and c_hash use the leftmost 128 bits
    // (half) of the SHA-2 hash of the ASCII-encoded value.
    // See OpenID Connect Core 1.0 §3.1.3.8.
    private static string ComputeHash(string value, string algorithm) {
        var       bytes  = Encoding.ASCII.GetBytes(value);
        using var hash   = CryptoProviderFactory.Default.CreateHashAlgorithm(GetHashAlgorithm(algorithm));
        var       hashed = hash.ComputeHash(bytes);
        return Base64UrlEncoder.Encode(hashed, 0, hashed.Length / 2);
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
