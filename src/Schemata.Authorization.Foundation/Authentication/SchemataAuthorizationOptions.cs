using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.IdentityModel.Tokens;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>
///     Configuration for the Schemata authorization server: token lifetimes, allowed
///     response types/modes, and endpoint URIs. Signing and encryption key material is
///     served from stored security rows under the issuer, not from options.
/// </summary>
public class SchemataAuthorizationOptions
{
    /// <summary>
    ///     OIDC subject identifier type:
    ///     <see cref="SubjectTypes.Public">"public"</see> or
    ///     <see cref="SubjectTypes.Pairwise">"pairwise"</see>,
    ///     per
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#SubjectIDTypes">
    ///         OpenID Connect Core 1.0 §8: Subject
    ///         Identifier Types
    ///     </seealso>
    ///     .
    /// </summary>
    /// <remarks>Effective only when the pairwise flow feature (<c>UsePairwiseSubjects()</c>) is installed.</remarks>
    public string SubjectType { get; set; } = SubjectTypes.Public;

    /// <summary>Salt for pairwise subject identifier computation when <see cref="SubjectType" /> is <c>pairwise</c>; read only by the pairwise flow feature.</summary>
    public string? PairwiseSalt { get; set; }

    /// <summary>Serialization format for access tokens (JWT, JWE, or opaque reference).</summary>
    public string AccessTokenFormat { get; set; } = TokenFormats.Jwe;

    /// <summary>Serialization format for refresh tokens (JWT, JWE, or opaque reference).</summary>
    public string RefreshTokenFormat { get; set; } = TokenFormats.Reference;

    /// <summary>Serialization format for interaction tokens used during consent flows.</summary>
    public string InteractionTokenFormat { get; set; } = TokenFormats.Reference;

    /// <summary>Validity duration of access tokens issued by the token endpoint.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Validity duration of ID tokens.</summary>
    public TimeSpan IdTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Validity duration of refresh tokens before they must be rotated.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Validity duration of interaction tokens used during consent/login flows.</summary>
    public TimeSpan InteractionTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     Validity duration of device codes before they expire,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.2">
    ///         RFC 8628: OAuth 2.0 Device Authorization
    ///         Grant §3.2: Device Authorization Response
    ///     </seealso>
    ///     .
    /// </summary>
    public TimeSpan DeviceCodeLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     Validity duration of authorization codes,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.2">
    ///         RFC 9700: The OAuth 2.0 Authorization
    ///         Framework: Best Current Practice §2.1.2
    ///     </seealso>
    ///     .
    /// </summary>
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     Minimum polling interval in seconds for the device code grant,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.5">
    ///         RFC 8628: OAuth 2.0 Device Authorization
    ///         Grant §3.5: Device Access Token Response
    ///     </seealso>
    ///     .
    /// </summary>
    public int DeviceCodeInterval { get; set; } = 5;


    /// <summary>
    ///     Token issuer identifier included in the "iss" claim,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9068.html#section-2.2">
    ///         RFC 9068: JSON Web Token (JWT) Profile
    ///         for OAuth 2.0 Access Tokens §2.2: Data Structure
    ///     </seealso>
    ///     .
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    ///     Default resource indicator used as the access token audience when no resource is requested,
    ///     falling back to the issuer when unset,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8707.html#section-2">
    ///         RFC 8707: Resource Indicators for OAuth 2.0 §2: Resource Parameter
    ///     </seealso>
    ///     and
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9068.html#section-2.2">
    ///         RFC 9068: JSON Web Token (JWT) Profile
    ///         for OAuth 2.0 Access Tokens §2.2: Data Structure
    ///     </seealso>
    ///     .
    /// </summary>
    public string? DefaultResource { get; set; }

    /// <summary>JWE content encryption algorithm (e.g., "A256CBC-HS512"); defaults to A256CBC-HS512.</summary>
    public string ContentEncryptionAlgorithm { get; set; } = ContentEncryptionAlgorithms.Aes256CbcHmacSha512;

    /// <summary>Absolute URI of the consent/login SPA that handles authorization interactions.</summary>
    public string? InteractionUri { get; set; }

    /// <summary>
    ///     Absolute URI where users enter device codes,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.3.1">
    ///         RFC 8628: OAuth 2.0 Device
    ///         Authorization Grant §3.3.1: Non-Textual Verification URI Optimization
    ///     </seealso>
    ///     .
    /// </summary>
    public string? DeviceVerificationUri { get; set; }

    /// <summary>Authentication scheme name for the bearer token handler.</summary>
    public string BearerScheme { get; set; } = SchemataAuthorizationSchemes.Bearer;

    /// <summary>Authentication scheme name for the authorization-code handler.</summary>
    public string CodeScheme { get; set; } = SchemataAuthorizationSchemes.Code;

    /// <summary>
    ///     Claim type for the OP session identifier on the authenticated user principal.
    ///     Defaults to "sid". Framework users who use a different claim type for their authentication
    ///     session can override this,
    ///     per
    ///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html#BCSupport">
    ///         OpenID Connect
    ///         Back-Channel Logout 1.0 §2.1: Indicating OP Support for Back-Channel Logout
    ///     </seealso>
    ///     .
    /// </summary>
    public string SessionIdClaimType { get; set; } = "sid";

    /// <summary>OAuth 2.0 response_type values the server will accept (e.g., "code", "code id_token").</summary>
    public HashSet<string> AllowedResponseTypes { get; } = [];

    /// <summary>Client authentication methods the server supports (e.g., "client_secret_post").</summary>
    public HashSet<string> AllowedClientAuthMethods { get; } = [
        ClientAuthMethods.ClientSecretBasic,
        ClientAuthMethods.ClientSecretPost
    ];

    /// <summary>
    ///     Trusted third-party assertion issuers for the
    ///     <c>urn:ietf:params:oauth:grant-type:jwt-bearer</c> grant: each entry maps an
    ///     assertion <c>iss</c> to the key verifying that issuer's signatures, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7523.html#section-3.1">
    ///         RFC 7523: JSON Web Token (JWT) Profile for OAuth 2.0 Client
    ///         Authentication and Authorization Grants §3.1: Authorization Grant Processing
    ///     </seealso>
    ///     . The table is the trust anchor: an assertion whose issuer has no entry is
    ///     rejected, and an empty map leaves the grant unusable.
    /// </summary>
    public Dictionary<string, SecurityKey> JwtBearerTrustedIssuers { get; } = new(StringComparer.Ordinal);

    /// <summary>Registers a trusted <c>jwt-bearer</c> assertion issuer with its verification key.</summary>
    /// <param name="issuer">Assertion <c>iss</c> value, compared with ordinal string equality.</param>
    /// <param name="key">Key verifying assertions issued by <paramref name="issuer" />.</param>
    public SchemataAuthorizationOptions AddJwtBearerTrustedIssuer(string issuer, SecurityKey key) {
        JwtBearerTrustedIssuers[issuer] = key;
        return this;
    }

    /// <summary>Response modes the server accepts (e.g., "query", "fragment", "form_post").</summary>
    public HashSet<string> AllowedResponseModes { get; } = [];

    /// <summary>Claim types advertised in the discovery document's claims_supported.</summary>
    public HashSet<string> SupportedClaims { get; } = [IdentityClaims.Subject];

    /// <summary>
    ///     Authentication Context Class References the deployment supports, per
    ///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderMetadata">
    ///         OpenID Connect Discovery 1.0
    ///         §3: OpenID Provider Metadata
    ///     </seealso>
    ///     . Advertised as the discovery <c>acr_values_supported</c> array; left empty, the
    ///     metadata member is omitted. Requested <c>acr_values</c> are voluntary
    ///     (OpenID Connect Core 1.0 §5.5.1.1): the login stamps the class the authentication
    ///     satisfied, so membership here advertises capability without enforcing matches.
    /// </summary>
    public HashSet<string> AcrValuesSupported { get; } = new(StringComparer.Ordinal);

    /// <summary>Permits a single response_type value (e.g., "code").</summary>
    public SchemataAuthorizationOptions PermitResponseType(string type) {
        AllowedResponseTypes.Add(type);
        return this;
    }

    /// <summary>Permits a two-value response_type combination (e.g., "code id_token").</summary>
    public SchemataAuthorizationOptions PermitResponseType((string first, string second) types) {
        var normalized = string.Join(' ', new[] { types.first, types.second }.OrderBy(x => x));
        AllowedResponseTypes.Add(normalized);
        return this;
    }

    /// <summary>Permits a three-value response_type combination (e.g., "code id_token token").</summary>
    public SchemataAuthorizationOptions PermitResponseType((string first, string second, string third) types) {
        var normalized = string.Join(' ', new[] { types.first, types.second, types.third }.OrderBy(x => x));
        AllowedResponseTypes.Add(normalized);
        return this;
    }

}
