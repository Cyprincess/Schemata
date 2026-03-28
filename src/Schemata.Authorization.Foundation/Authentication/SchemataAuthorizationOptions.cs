using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Authentication;

public class SchemataAuthorizationOptions
{
    /// <summary>OIDC subject identifier type: "public" or "pairwise" (OIDC Core §8).</summary>
    public string SubjectType { get; set; } = SubjectTypes.Public;

    /// <summary>Salt used to compute pairwise subject identifiers; ignored when SubjectType is "public".</summary>
    public string? PairwiseSalt { get; set; }

    /// <summary>Serialization format for access tokens (JWT, JWE, or opaque reference).</summary>
    public string AccessTokenFormat { get; set; } = TokenFormats.Jwe;

    /// <summary>Serialization format for refresh tokens (JWT, JWE, or opaque reference).</summary>
    public string RefreshTokenFormat { get; set; } = TokenFormats.Reference;

    /// <summary>Serialization format for interaction tokens used during consent flows.</summary>
    public string InteractionTokenFormat { get; set; } = TokenFormats.Reference;

    /// <summary>Validity duration of access tokens issued by the token endpoint.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Validity duration of ID tokens (OIDC Core §2).</summary>
    public TimeSpan IdTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Validity duration of refresh tokens before they must be rotated.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Validity duration of interaction tokens used during consent/login flows.</summary>
    public TimeSpan InteractionTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Validity duration of device codes before they expire (RFC 8628 §3.2).</summary>
    public TimeSpan DeviceCodeLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Validity duration of authorization codes (RFC 9700 §2.1.2).</summary>
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Minimum polling interval in seconds for the device code grant (RFC 8628 §3.5).</summary>
    public int DeviceCodeInterval { get; set; } = 5;

    /// <summary>Token issuer identifier included in the "iss" claim (RFC 9068 §2.2).</summary>
    public string? Issuer { get; set; }

    /// <summary>Asymmetric or symmetric key used to sign JWTs.</summary>
    public SecurityKey? SigningKey { get; set; }

    /// <summary>JWS algorithm identifier (e.g., "RS256"); auto-detected from the key when null.</summary>
    public string? SigningAlgorithm { get; set; }

    /// <summary>Key used to encrypt JWE access tokens; null disables JWE.</summary>
    public SecurityKey? EncryptionKey { get; set; }

    /// <summary>JWE key-management algorithm (e.g., "RSA-OAEP"); required when EncryptionKey is set.</summary>
    public string? EncryptionAlgorithm { get; set; }

    /// <summary>JWE content encryption algorithm (e.g., "A256CBC-HS512"); defaults to A256CBC-HS512.</summary>
    public string ContentEncryptionAlgorithm { get; set; } = ContentEncryptionAlgorithms.Aes256CbcHmacSha512;

    /// <summary>Absolute URI of the consent/login SPA that handles authorization interactions.</summary>
    public string? InteractionUri { get; set; }

    /// <summary>Absolute URI where users enter device codes (RFC 8628 §3.3.1 verification_uri).</summary>
    public string? DeviceVerificationUri { get; set; }

    /// <summary>Authentication scheme name used to register the bearer token handler.</summary>
    public string BearerScheme { get; set; } = SchemataAuthorizationSchemes.Bearer;

    /// <summary>Authentication scheme name used to register the authorization-code handler for authorization-endpoint flows.</summary>
    public string CodeScheme { get; set; } = SchemataAuthorizationSchemes.Code;

    /// <summary>
    ///     Claim type used to read the OP session identifier from the authenticated user principal.
    ///     Defaults to "sid" per OIDC Back-Channel Logout §2.1. Framework users who use a different
    ///     claim type for their authentication session can override this.
    /// </summary>
    public string SessionIdClaimType { get; set; } = "sid";

    /// <summary>OAuth 2.0 response_type values the server will accept (e.g., "code", "code id_token").</summary>
    public HashSet<string> AllowedResponseTypes { get; } = [];

    /// <summary>Client authentication methods the server supports (e.g., "client_secret_post").</summary>
    public HashSet<string> AllowedClientAuthMethods { get; } = [];

    /// <summary>Response modes the server accepts (e.g., "query", "fragment", "form_post").</summary>
    public HashSet<string> AllowedResponseModes { get; } = [];

    /// <summary>Claim types advertised in the discovery document's claims_supported.</summary>
    public HashSet<string> SupportedClaims { get; } = [];

    /// <summary>Scope values the server is willing to grant; scopes not in this set are rejected.</summary>
    public HashSet<string> AllowedScopes { get; } = [];

    public SchemataAuthorizationOptions PermitResponseType(string type) {
        AllowedResponseTypes.Add(type);
        return this;
    }

    public SchemataAuthorizationOptions PermitResponseType((string first, string second) types) {
        var normalized = string.Join(' ', new[] { types.first, types.second }.OrderBy(x => x));
        AllowedResponseTypes.Add(normalized);
        return this;
    }

    public SchemataAuthorizationOptions PermitResponseType((string first, string second, string third) types) {
        var normalized = string.Join(' ', new[] { types.first, types.second, types.third }.OrderBy(x => x));
        AllowedResponseTypes.Add(normalized);
        return this;
    }

    public SchemataAuthorizationOptions AddEphemeralSigningKey(string algorithm = SigningAlgorithms.RsaSha256) {
        SigningKey = algorithm switch {
            SigningAlgorithms.RsaSha256 or SigningAlgorithms.RsaSha384 or SigningAlgorithms.RsaSha512 => new RsaSecurityKey(RSA.Create(2048)),
            SigningAlgorithms.EcdsaSha256 => new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)),
            SigningAlgorithms.EcdsaSha384 => new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP384)),
            SigningAlgorithms.EcdsaSha512 => new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP521)),
            SigningAlgorithms.RsaPssSha256 or SigningAlgorithms.RsaPssSha384 or SigningAlgorithms.RsaPssSha512 => new RsaSecurityKey(RSA.Create(2048)),
            SigningAlgorithms.HmacSha256 => new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)),
            SigningAlgorithms.HmacSha384 => new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(48)),
            SigningAlgorithms.HmacSha512 => new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)),
            var _ => throw new ArgumentException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1014), algorithm), nameof(algorithm)),
        };
        SigningAlgorithm = algorithm;
        return this;
    }

    public SchemataAuthorizationOptions AddEphemeralEncryptionKey(string algorithm = EncryptionAlgorithms.RsaOaep) {
        EncryptionKey = algorithm switch {
            EncryptionAlgorithms.RsaOaep or EncryptionAlgorithms.RsaOaep256 => new RsaSecurityKey(RSA.Create(2048)),
            EncryptionAlgorithms.A128Kw => new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(16)),
            EncryptionAlgorithms.A192Kw => new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(24)),
            EncryptionAlgorithms.A256Kw => new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)),
            var _ => throw new ArgumentException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1014), algorithm), nameof(algorithm)),
        };
        EncryptionAlgorithm = algorithm;
        return this;
    }
}
