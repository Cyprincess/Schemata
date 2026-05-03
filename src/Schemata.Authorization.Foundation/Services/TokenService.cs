using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Authentication;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Core token creation and validation service.
///     Creates signed JWTs, encrypted JWEs, opaque reference tokens, and OIDC
///     ID tokens.  Validates tokens against the configured signing key and issuer.
///     All token claims include <c>iss</c>, <c>iat</c>, <c>exp</c>, and <c>jti</c>.
/// </summary>
public class TokenService
{
    private readonly string                       _algorithm;
    private readonly EncryptingCredentials?       _encrypting;
    private readonly JsonWebTokenHandler          _handler = new() { SetDefaultTimesOnTokenCreation = false };
    private readonly SchemataAuthorizationOptions _options;
    private readonly SigningCredentials           _signing;
    private readonly TokenValidationParameters    _validation;

    /// <summary>
    ///     Initializes the token service from <see cref="SchemataAuthorizationOptions" />.
    ///     Configures signing credentials, optional encryption credentials, and
    ///     validation parameters.
    /// </summary>
    /// <param name="options">Server authorization options.</param>
    public TokenService(IOptions<SchemataAuthorizationOptions> options) {
        _options   = options.Value;
        _algorithm = _options.SigningAlgorithm!;

        _signing = new(_options.SigningKey!, _algorithm);

        if (_options.EncryptionKey is not null) {
            _encrypting = new(_options.EncryptionKey, _options.EncryptionAlgorithm!, _options.ContentEncryptionAlgorithm);
        }

        _validation = new() {
            ValidIssuer        = _options.Issuer,
            ValidateAudience   = false,
            IssuerSigningKey   = _signing.Key,
            TokenDecryptionKey = _options.EncryptionKey,
            ClockSkew          = TimeSpan.FromMinutes(1),
        };
    }

    /// <summary>
    ///     Creates a signed JWT (or encrypted JWE when <paramref name="encrypt" /> is <c>true</c>).
    ///     Sets <c>iss</c>, <c>iat</c>, <c>exp</c> automatically.
    /// </summary>
    /// <param name="claims">Claims to embed in the token.</param>
    /// <param name="lifetime">Token validity duration.</param>
    /// <param name="encrypt">When <c>true</c>, wraps the JWT as a JWE.</param>
    public string CreateToken(IEnumerable<Claim> claims, TimeSpan lifetime, bool encrypt = false) {
        if (encrypt && _encrypting is null) {
            throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1016), "Encryption key"));
        }

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor {
            Subject               = new(claims),
            Expires               = now + lifetime,
            IssuedAt              = now,
            Issuer                = _options.Issuer,
            SigningCredentials    = _signing,
            EncryptingCredentials = encrypt ? _encrypting : null,
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
    public string CreateIdToken(
        List<Claim> claims,
        TimeSpan    lifetime,
        string?     at    = null,
        string?     code  = null,
        string?     nonce = null
    ) {
        claims.Add(new(Claims.TokenUse, "id_token"));

        if (!string.IsNullOrWhiteSpace(nonce)) {
            claims.Add(new(Claims.Nonce, nonce));
        }

        if (!string.IsNullOrWhiteSpace(at)) {
            claims.Add(new(Claims.AtHash, ComputeHash(at, _algorithm)));
        }

        if (!string.IsNullOrWhiteSpace(code)) {
            claims.Add(new(Claims.CHash, ComputeHash(code, _algorithm)));
        }

        return CreateToken(claims, lifetime);
    }

    /// <summary>
    ///     Validates a JWT or JWE token string against the configured issuer
    ///     and signing key. When <paramref name="audience" /> is provided,
    ///     audience validation is enforced.
    /// </summary>
    /// <param name="token">The JWT/JWE token string, or stored payload for reference tokens.</param>
    /// <param name="audience">Expected audience (client ID); null disables audience validation.</param>
    /// <param name="lifetime">When <c>false</c>, expired tokens are still accepted (used for refresh token inspection).</param>
    public async Task<ClaimsPrincipal?> Validate(string? token, string? audience = null, bool lifetime = true) {
        if (string.IsNullOrWhiteSpace(token)) {
            return null;
        }

        var parameters = _validation.Clone();

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
        using var hash   = CryptoProviderFactory.Default.CreateHashAlgorithm(algorithm);
        var       hashed = hash.ComputeHash(bytes);
        return Base64UrlEncoder.Encode(hashed, 0, hashed.Length / 2);
    }
}
