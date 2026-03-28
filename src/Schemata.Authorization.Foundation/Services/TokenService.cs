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

public class TokenService
{
    private readonly string                       _algorithm;
    private readonly EncryptingCredentials?       _encrypting;
    private readonly JsonWebTokenHandler          _handler = new() { SetDefaultTimesOnTokenCreation = false };
    private readonly SchemataAuthorizationOptions _options;
    private readonly SigningCredentials           _signing;
    private readonly TokenValidationParameters    _validation;

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

    public string CreateReference() { return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)); }

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

    private static string ComputeHash(string value, string algorithm) {
        var       bytes  = Encoding.ASCII.GetBytes(value);
        using var hash   = CryptoProviderFactory.Default.CreateHashAlgorithm(algorithm);
        var       hashed = hash.ComputeHash(bytes);
        return Base64UrlEncoder.Encode(hashed, 0, hashed.Length / 2);
    }
}
