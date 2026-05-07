using System.Collections.Generic;

namespace Schemata.Abstractions;

/// <summary>
///     Well-known constant values used across the Schemata framework.
/// </summary>
public static class SchemataConstants
{
    /// <summary>
    ///     Schemata framework identifier as a GUID string.
    /// </summary>
    public const string Schemata = "9049a32e-c96b-4e0e-ae34-c370c574f00d";

    #region Nested type: ApplicationTypes

    /// <summary>
    ///     OAuth 2.0 application type identifiers.
    /// </summary>
    public static class ApplicationTypes
    {
        /// <summary>A native (desktop/mobile) application.</summary>
        public const string Native = "native";

        /// <summary>A web-based application.</summary>
        public const string Web = "web";
    }

    #endregion

    #region Nested type: AuthorizationTypes

    /// <summary>
    ///     Distinguishes consent-reusable records from
    ///     single-grant anchors so the consent advisor only matches the former.
    /// </summary>
    public static class AuthorizationTypes
    {
        /// <summary>
        ///     Single-grant ad-hoc record produced by the authorization-code flow's
        ///     interactive consent. Reusable for silent consent on subsequent /authorize
        ///     calls from the same browser-bound resource owner.
        /// </summary>
        public const string AdHoc = "ad-hoc";

        /// <summary>
        ///     Single-grant anchor produced by the device flow's user_code approval.
        ///     NOT reusable for silent consent because the verifying user agent is not
        ///     the requesting device — the consent advisor must skip these records.
        /// </summary>
        public const string Device = "device";

        /// <summary>
        ///     Persistent consent record explicitly stored for cross-session reuse.
        /// </summary>
        public const string Permanent = "permanent";
    }

    #endregion

    #region Nested type: ClaimDestinations

    /// <summary>
    ///     Claim destination identifiers controlling which tokens receive which claims.
    /// </summary>
    public static class ClaimDestinations
    {
        /// <summary>Include the claim in the access token.</summary>
        public const string AccessToken = "access_token";

        /// <summary>Include the claim in the ID token.</summary>
        public const string IdentityToken = "id_token";

        /// <summary>Include the claim in the UserInfo response.</summary>
        public const string UserInfo = "userinfo";
    }

    #endregion

    #region Nested type: Claims

    /// <summary>
    ///     Claim names including registered JWT claims, as registered in the
    ///     <seealso href="https://www.iana.org/assignments/jwt/jwt.xhtml">IANA JSON Web Token Claims</seealso> registry.
    /// </summary>
    public static class Claims
    {
        public const string Address             = "address";
        public const string AtHash              = "at_hash";
        public const string Audience            = "aud";
        public const string Birthdate           = "birthdate";
        public const string CHash               = "c_hash";
        public const string ClientId            = "client_id";
        public const string Email               = "email";
        public const string EmailVerified       = "email_verified";
        public const string Events              = "events";
        public const string Expiration          = "exp";
        public const string FamilyName          = "family_name";
        public const string Gender              = "gender";
        public const string GivenName           = "given_name";
        public const string IssuedAt            = "iat";
        public const string Issuer              = "iss";
        public const string JwtId               = "jti";
        public const string Locale              = "locale";
        public const string MiddleName          = "middle_name";
        public const string Name                = "name";
        public const string Nickname            = "nickname";
        public const string Nonce               = "nonce";
        public const string NotBefore           = "nbf";
        public const string PhoneNumber         = "phone_number";
        public const string PhoneNumberVerified = "phone_number_verified";
        public const string Picture             = "picture";
        public const string PreferredUsername   = "preferred_username";
        public const string Profile             = "profile";
        public const string Role                = "role";
        public const string Scope               = "scope";
        public const string SecurityStamp       = "security_stamp";
        public const string SessionId           = "sid";
        public const string Subject             = "sub";
        public const string TokenUse            = "token_use";
        public const string UpdatedAt           = "updated_at";
        public const string Website             = "website";
        public const string Zoneinfo            = "zoneinfo";
        public const string AuthTime            = "auth_time";
    }

    #endregion

    #region Nested type: ClientAuthMethods

    /// <summary>
    ///     OAuth 2.0 client authentication method identifiers, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.3: Client Authentication
    ///     </seealso>
    ///     .
    /// </summary>
    public static class ClientAuthMethods
    {
        /// <summary>
        ///     Client authenticates via HTTP Basic authentication, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §2.3.1: Client Password
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ClientSecretBasic = "client_secret_basic";

        /// <summary>
        ///     Client authenticates by including credentials in the request body, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §2.3.1: Client Password
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ClientSecretPost = "client_secret_post";
    }

    #endregion

    #region Nested type: ClientTypes

    /// <summary>
    ///     OAuth 2.0 client type identifiers.
    /// </summary>
    public static class ClientTypes
    {
        /// <summary>A confidential client that can maintain the confidentiality of its credentials.</summary>
        public const string Confidential = "confidential";

        /// <summary>A public client that cannot maintain the confidentiality of its credentials.</summary>
        public const string Public = "public";
    }

    #endregion

    #region Nested type: ConsentTypes

    /// <summary>
    ///     OAuth 2.0 consent type identifiers.
    /// </summary>
    public static class ConsentTypes
    {
        /// <summary>The user must explicitly grant consent.</summary>
        public const string Explicit = "explicit";

        /// <summary>Consent is managed by an external system.</summary>
        public const string External = "external";

        /// <summary>Consent is implicitly granted.</summary>
        public const string Implicit = "implicit";
    }

    #endregion

    #region Nested type: ContentEncryptionAlgorithms

    /// <summary>
    ///     JWE content encryption algorithm identifiers, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-5.1">
    ///         RFC 7518: JSON Web Algorithms (JWA) §5.1: "enc" (Encryption Algorithm) Header Parameter Values for JWE
    ///     </seealso>
    ///     .
    /// </summary>
    public static class ContentEncryptionAlgorithms
    {
        /// <summary>
        ///     AES_128_CBC_HMAC_SHA_256 authenticated encryption, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-5.2.3">
        ///         RFC 7518: JSON Web Algorithms (JWA)
        ///         §5.2.3: AES_128_CBC_HMAC_SHA_256
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Aes128CbcHmacSha256 = "A128CBC-HS256";

        /// <summary>
        ///     AES_192_CBC_HMAC_SHA_384 authenticated encryption, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-5.2.4">
        ///         RFC 7518: JSON Web Algorithms (JWA)
        ///         §5.2.4: AES_192_CBC_HMAC_SHA_384
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Aes192CbcHmacSha384 = "A192CBC-HS384";

        /// <summary>
        ///     AES_256_CBC_HMAC_SHA_512 authenticated encryption, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-5.2.5">
        ///         RFC 7518: JSON Web Algorithms (JWA)
        ///         §5.2.5: AES_256_CBC_HMAC_SHA_512
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Aes256CbcHmacSha512 = "A256CBC-HS512";

        /// <summary>
        ///     AES-GCM with 128-bit key, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-5.3">
        ///         RFC 7518: JSON Web Algorithms (JWA) §5.3:
        ///         Content Encryption with AES GCM
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Aes128Gcm = "A128GCM";

        /// <summary>
        ///     AES-GCM with 192-bit key, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-5.3">
        ///         RFC 7518: JSON Web Algorithms (JWA) §5.3:
        ///         Content Encryption with AES GCM
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Aes192Gcm = "A192GCM";

        /// <summary>
        ///     AES-GCM with 256-bit key, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-5.3">
        ///         RFC 7518: JSON Web Algorithms (JWA) §5.3:
        ///         Content Encryption with AES GCM
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Aes256Gcm = "A256GCM";
    }

    #endregion

    #region Nested type: EncryptionAlgorithms

    /// <summary>
    ///     JWE key management algorithm identifiers, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-4.1">
    ///         RFC 7518: JSON Web Algorithms (JWA) §4.1: "alg" (Algorithm) Header Parameter Values for JWE
    ///     </seealso>
    ///     .
    /// </summary>
    public static class EncryptionAlgorithms
    {
        /// <summary>
        ///     RSAES OAEP using default parameters, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-4.3">
        ///         RFC 7518: JSON Web Algorithms (JWA) §4.3:
        ///         Key Encryption with RSAES OAEP
        ///     </seealso>
        ///     .
        /// </summary>
        public const string RsaOaep = "RSA-OAEP";

        /// <summary>
        ///     RSAES OAEP using SHA-256 and MGF1 with SHA-256, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-4.3">
        ///         RFC 7518: JSON Web Algorithms (JWA) §4.3:
        ///         Key Encryption with RSAES OAEP
        ///     </seealso>
        ///     .
        /// </summary>
        public const string RsaOaep256 = "RSA-OAEP-256";

        /// <summary>
        ///     Elliptic Curve Diffie-Hellman Ephemeral Static key agreement, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-4.6">
        ///         RFC 7518: JSON Web Algorithms (JWA) §4.6:
        ///         Key Agreement with ECDH-ES
        ///     </seealso>
        ///     .
        /// </summary>
        public const string EcdhEs = "ECDH-ES";

        /// <summary>
        ///     ECDH-ES using Concat KDF and CEK wrapped with "A128KW", per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-4.6">
        ///         RFC 7518: JSON Web Algorithms (JWA) §4.6:
        ///         Key Agreement with ECDH-ES
        ///     </seealso>
        ///     .
        /// </summary>
        public const string EcdhEsA128Kw = "ECDH-ES+A128KW";

        /// <summary>
        ///     ECDH-ES using Concat KDF and CEK wrapped with "A192KW", per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-4.6">
        ///         RFC 7518: JSON Web Algorithms (JWA) §4.6:
        ///         Key Agreement with ECDH-ES
        ///     </seealso>
        ///     .
        /// </summary>
        public const string EcdhEsA192Kw = "ECDH-ES+A192KW";

        /// <summary>
        ///     ECDH-ES using Concat KDF and CEK wrapped with "A256KW", per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-4.6">
        ///         RFC 7518: JSON Web Algorithms (JWA) §4.6:
        ///         Key Agreement with ECDH-ES
        ///     </seealso>
        ///     .
        /// </summary>
        public const string EcdhEsA256Kw = "ECDH-ES+A256KW";

        /// <summary>
        ///     AES Key Wrap with 128-bit key, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-4.4">
        ///         RFC 7518: JSON Web Algorithms (JWA) §4.4:
        ///         Key Wrapping with AES Key Wrap
        ///     </seealso>
        ///     .
        /// </summary>
        public const string A128Kw = "A128KW";

        /// <summary>
        ///     AES Key Wrap with 192-bit key, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-4.4">
        ///         RFC 7518: JSON Web Algorithms (JWA) §4.4:
        ///         Key Wrapping with AES Key Wrap
        ///     </seealso>
        ///     .
        /// </summary>
        public const string A192Kw = "A192KW";

        /// <summary>
        ///     AES Key Wrap with 256-bit key, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-4.4">
        ///         RFC 7518: JSON Web Algorithms (JWA) §4.4:
        ///         Key Wrapping with AES Key Wrap
        ///     </seealso>
        ///     .
        /// </summary>
        public const string A256Kw = "A256KW";
    }

    #endregion

    #region Nested type: Endpoints

    /// <summary>
    ///     Well-known OAuth 2.0 / OpenID Connect endpoint paths served by Schemata.
    /// </summary>
    public static class Endpoints
    {
        /// <summary>Token endpoint path.</summary>
        public const string Token = "/Connect/Token";

        /// <summary>UserInfo / profile endpoint path.</summary>
        public const string Profile = "/Connect/Profile";

        /// <summary>Authorization endpoint path.</summary>
        public const string Authorize = "/Connect/Authorize";

        /// <summary>
        ///     Device authorization endpoint path, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html">RFC 8628: OAuth 2.0 Device Authorization Grant</seealso>
        ///     .
        /// </summary>
        public const string Device = "/Connect/Device";

        /// <summary>
        ///     Token introspection endpoint path, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html">RFC 7662: OAuth 2.0 Token Introspection</seealso>.
        /// </summary>
        public const string Introspect = "/Connect/Introspect";

        /// <summary>
        ///     Token revocation endpoint path, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html">RFC 7009: OAuth 2.0 Token Revocation</seealso>.
        /// </summary>
        public const string Revoke = "/Connect/Revoke";

        /// <summary>
        ///     RP-Initiated Logout endpoint path, per
        ///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">OpenID Connect RP-Initiated Logout 1.0</seealso>
        ///     .
        /// </summary>
        public const string EndSession = "/Connect/EndSession";

        /// <summary>OpenID Connect Discovery document endpoint path.</summary>
        public const string Discovery = "openid-configuration";

        /// <summary>JWKS (JSON Web Key Set) endpoint path.</summary>
        public const string Jwks = "jwks";
    }

    #endregion

    #region Nested type: ErrorCodes

    /// <summary>
    ///     Standard machine-readable error codes following the Google API error model.
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>No error.</summary>
        public const string Ok = "OK";

        /// <summary>The request contained invalid arguments.</summary>
        public const string InvalidArgument = "INVALID_ARGUMENT";

        /// <summary>The requested resource was not found.</summary>
        public const string NotFound = "NOT_FOUND";

        /// <summary>The caller does not have permission.</summary>
        public const string PermissionDenied = "PERMISSION_DENIED";

        /// <summary>The operation was aborted (e.g., concurrency conflict).</summary>
        public const string Aborted = "ABORTED";

        /// <summary>The resource already exists.</summary>
        public const string AlreadyExists = "ALREADY_EXISTS";

        /// <summary>A precondition for the operation was not met.</summary>
        public const string FailedPrecondition = "FAILED_PRECONDITION";

        /// <summary>The caller is not authenticated.</summary>
        public const string Unauthenticated = "UNAUTHENTICATED";

        /// <summary>A quota or rate limit was exceeded.</summary>
        public const string ResourceExhausted = "RESOURCE_EXHAUSTED";

        /// <summary>An internal server error occurred.</summary>
        public const string Internal = "INTERNAL";
    }

    #endregion

    #region Nested type: ErrorReasons

    /// <summary>
    ///     Machine-readable reason codes for structured error details.
    /// </summary>
    public static class ErrorReasons
    {
        /// <summary>The concurrency token did not match.</summary>
        public const string ConcurrencyMismatch = "CONCURRENCY_MISMATCH";
    }

    #endregion

    #region Nested type: EventTypes

    /// <summary>
    ///     Event identifiers.
    /// </summary>
    public static class EventTypes
    {
        /// <summary>
        ///     Back-Channel Logout event URI, per
        ///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html#LogoutToken">
        ///         OpenID Connect Back-Channel Logout 1.0
        ///         §2.4
        ///     </seealso>
        ///     .
        /// </summary>
        public const string LogoutEvent = "http://schemas.openid.net/event/backchannel-logout";
    }

    #endregion

    #region Nested type: FieldReasons

    /// <summary>
    ///     Machine-readable reason codes for field-level validation violations.
    /// </summary>
    public static class FieldReasons
    {
        /// <summary>The field must not be empty.</summary>
        public const string NotEmpty = "not_empty";

        /// <summary>The request payload is invalid.</summary>
        public const string InvalidPayload = "invalid_payload";

        /// <summary>The filter expression is invalid.</summary>
        public const string InvalidFilter = "invalid_filter";

        /// <summary>The resource name is invalid.</summary>
        public const string InvalidName = "invalid_name";

        /// <summary>The order_by expression is invalid.</summary>
        public const string InvalidOrderBy = "invalid_order_by";

        /// <summary>The page token is invalid or expired.</summary>
        public const string InvalidPageToken = "invalid_page_token";

        /// <summary>Cross-parent operations are not supported.</summary>
        public const string CrossParentUnsupported = "cross_parent_unsupported";
    }

    #endregion

    #region Nested type: GrantTypes

    /// <summary>
    ///     OAuth 2.0 grant type identifiers, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
    ///     and extensions.
    /// </summary>
    public static class GrantTypes
    {
        /// <summary>
        ///     Authorization code grant, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1: Authorization Code Grant
        ///     </seealso>
        ///     .
        /// </summary>
        public const string AuthorizationCode = "authorization_code";

        /// <summary>
        ///     Client credentials grant, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.4">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.4: Client Credentials Grant
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ClientCredentials = "client_credentials";

        /// <summary>
        ///     Refresh token grant, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-6">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §6: Refreshing an Access Token
        ///     </seealso>
        ///     .
        /// </summary>
        public const string RefreshToken = "refresh_token";

        /// <summary>
        ///     Device authorization grant, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html">RFC 8628: OAuth 2.0 Device Authorization Grant</seealso>
        ///     .
        /// </summary>
        public const string DeviceCode = "urn:ietf:params:oauth:grant-type:device_code";

        /// <summary>
        ///     Token exchange grant, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html">RFC 8693: OAuth 2.0 Token Exchange</seealso>.
        /// </summary>
        public const string TokenExchange = "urn:ietf:params:oauth:grant-type:token-exchange";
    }

    #endregion

    #region Nested type: InteractionTypes

    /// <summary>
    ///     Well-known interaction type identifiers used during authorization UI flows.
    /// </summary>
    public static class InteractionTypes
    {
        /// <summary>An authorization consent interaction.</summary>
        public const string Authorize = "authorize";

        /// <summary>A device authorization interaction.</summary>
        public const string Device = "device";
    }

    #endregion

    #region Nested type: Keys

    /// <summary>
    ///     Option keys and cache keys.
    /// </summary>
    public static class Keys
    {
        /// <summary>Key for the features dictionary in SchemataOptions.</summary>
        public const string Features = "Features";

        /// <summary>Key for the modular modules list in configuration.</summary>
        public const string ModularModules = "Modular:Modules";

        /// <summary>Key for Authorization.</summary>
        public const string Authorization = "authorization";

        /// <summary>Key for Entity.</summary>
        public const string Entity = "entity";

        /// <summary>Key for Resource.</summary>
        public const string Resource = "resource";
        
        /// <summary>Key for Tenancy.</summary>
        public const string Tenancy = "tenancy";
    }

    #endregion

    #region Nested type: OAuthErrors

    /// <summary>
    ///     OAuth 2.0 error codes, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §5.2: Error Response
    ///     </seealso>
    ///     and extensions.
    /// </summary>
    public static class OAuthErrors
    {
        /// <summary>
        ///     The provided authorization grant or refresh token is invalid, expired, revoked, or does not match, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §5.2: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string InvalidGrant = "invalid_grant";

        /// <summary>
        ///     Client authentication failed, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §5.2: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string InvalidClient = "invalid_client";

        /// <summary>
        ///     The client is not authorized to request an authorization code using this method, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.2.1: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string UnauthorizedClient = "unauthorized_client";

        /// <summary>
        ///     The requested scope is invalid, unknown, or malformed, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §5.2: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string InvalidScope = "invalid_scope";

        /// <summary>
        ///     The request is missing a required parameter or is otherwise malformed, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.2.1: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string InvalidRequest = "invalid_request";

        /// <summary>
        ///     The resource owner or authorization server denied the request, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.2.1: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string AccessDenied = "access_denied";

        /// <summary>
        ///     The authorization server does not support the requested grant type, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §5.2: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string UnsupportedGrantType = "unsupported_grant_type";

        /// <summary>
        ///     The authorization server does not support the requested response type, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.2.1: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string UnsupportedResponseType = "unsupported_response_type";

        /// <summary>The redirect URI is invalid or does not match a registered URI.</summary>
        public const string InvalidRedirectUri = "invalid_redirect_uri";

        /// <summary>
        ///     The token presented has expired, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.5">
        ///         RFC 8628: OAuth 2.0 Device Authorization
        ///         Grant §3.5: Authorization Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ExpiredToken = "expired_token";

        /// <summary>
        ///     The authorization request is still pending, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.5">
        ///         RFC 8628: OAuth 2.0 Device Authorization
        ///         Grant §3.5: Authorization Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string AuthorizationPending = "authorization_pending";

        /// <summary>
        ///     The polling interval must be increased, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.5">
        ///         RFC 8628: OAuth 2.0 Device Authorization
        ///         Grant §3.5: Authorization Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string SlowDown = "slow_down";

        /// <summary>
        ///     The authorization server encountered an unexpected condition, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.2.1: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ServerError = "server_error";

        /// <summary>
        ///     The authorization server requires end-user authentication, per
        ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthError">
        ///         OpenID Connect Core 1.0 §3.1.2.6:
        ///         Authentication Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string LoginRequired = "login_required";

        /// <summary>
        ///     The authorization server requires end-user consent, per
        ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthError">
        ///         OpenID Connect Core 1.0 §3.1.2.6:
        ///         Authentication Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ConsentRequired = "consent_required";
    }

    #endregion

    #region Nested type: Orders

    /// <summary>
    ///     Well-known ordering constants for feature and advisor pipeline sequencing.
    /// </summary>
    public static class Orders
    {
        /// <summary>Base anchor for built-in feature and advisor ordering chains.</summary>
        public const int Base = 100_000_000;

        /// <summary>Base anchor for extension feature ordering chains.</summary>
        public const int Extension = Base + 300_000_000;

        /// <summary>Terminal anchor for advisors and features that must run near the end of a pipeline.</summary>
        public const int Max = 900_000_000;
    }

    #endregion

    #region Nested type: Parameters

    /// <summary>
    ///     Well-known parameter names used in serialization, API conventions, and OAuth form/query parameters.
    /// </summary>
    public static class Parameters
    {
        /// <summary>The entity tag parameter name.</summary>
        public const string EntityTag = "etag";

        /// <summary>The resource name parameter name.</summary>
        public const string Name = "name";

        /// <summary>The type discriminator parameter name for polymorphic serialization.</summary>
        public const string Type = "@type";

        /// <summary>
        ///     OAuth access_token response parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.4">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.4: Access Token Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string AccessToken = "access_token";

        /// <summary>
        ///     OAuth grant_type parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.3: Access Token Request
        ///     </seealso>
        ///     .
        /// </summary>
        public const string GrantType = "grant_type";

        /// <summary>
        ///     OAuth client_id parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.2">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §2.2: Client Identifier
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ClientId = "client_id";

        /// <summary>
        ///     OAuth client_secret parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §2.3.1: Client Password
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ClientSecret = "client_secret";

        /// <summary>
        ///     OAuth authorization code parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.3: Access Token Request
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Code = "code";

        /// <summary>Schemata interaction code_type parameter identifying the token type URI of the code.</summary>
        public const string CodeType = "code_type";

        /// <summary>
        ///     OAuth error response parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §5.2: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Error = "error";

        /// <summary>
        ///     OAuth error_description response parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §5.2: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ErrorDescription = "error_description";

        /// <summary>
        ///     OAuth expires_in response parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.4">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.4: Access Token Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ExpiresIn = "expires_in";

        /// <summary>
        ///     OAuth id_token response parameter, per
        ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#TokenResponse">
        ///         OpenID Connect Core 1.0
        ///         §3.1.3.3: Token Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string IdToken = "id_token";

        /// <summary>
        ///     OAuth logout_token parameter, per
        ///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html#LogoutToken">
        ///         OpenID Connect Back-Channel Logout 1.0
        ///         §2.4
        ///     </seealso>
        ///     .
        /// </summary>
        public const string LogoutToken = "logout_token";

        /// <summary>
        ///     OAuth response_type parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.1.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §3.1.1: Response Type
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ResponseType = "response_type";

        /// <summary>
        ///     OpenID Connect nonce parameter, per
        ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
        ///         OpenID Connect Core 1.0 §3.1.2.1:
        ///         Authentication Request
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Nonce = "nonce";

        /// <summary>
        ///     Device code parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.4">
        ///         RFC 8628: OAuth 2.0 Device Authorization
        ///         Grant §3.4: Device Access Token Request
        ///     </seealso>
        ///     .
        /// </summary>
        public const string DeviceCode = "device_code";

        /// <summary>
        ///     Device user code parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.2">
        ///         RFC 8628: OAuth 2.0 Device Authorization
        ///         Grant §3.2: Device Authorization Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string UserCode = "user_code";

        /// <summary>
        ///     Token parameter used in introspection and revocation, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html#section-2.1">
        ///         RFC 7662: OAuth 2.0 Token Introspection
        ///         §2.1: Introspection Request
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Token = "token";

        /// <summary>
        ///     Refresh token parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-6">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §6: Refreshing an Access Token
        ///     </seealso>
        ///     .
        /// </summary>
        public const string RefreshToken = "refresh_token";

        /// <summary>
        ///     OAuth state parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.1">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.1: Authorization Request
        ///     </seealso>
        ///     .
        /// </summary>
        public const string State = "state";

        /// <summary>
        ///     Token exchange subject_token parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html#section-2.1">
        ///         RFC 8693: OAuth 2.0 Token Exchange §2.1:
        ///         Request
        ///     </seealso>
        ///     .
        /// </summary>
        public const string SubjectToken = "subject_token";

        /// <summary>
        ///     Token exchange subject_token_type parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html#section-2.1">
        ///         RFC 8693: OAuth 2.0 Token Exchange §2.1:
        ///         Request
        ///     </seealso>
        ///     .
        /// </summary>
        public const string SubjectTokenType = "subject_token_type";

        /// <summary>
        ///     PKCE code_challenge parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html#section-4.3">
        ///         RFC 7636: Proof Key for Code Exchange by OAuth Public Clients (PKCE) §4.3: Authorization Server Stores the Code
        ///         Challenge
        ///     </seealso>
        ///     .
        /// </summary>
        public const string CodeChallenge = "code_challenge";

        /// <summary>
        ///     PKCE code_challenge_method parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html#section-4.3">
        ///         RFC 7636: Proof Key for Code Exchange by OAuth Public Clients (PKCE) §4.3: Authorization Server Stores the Code
        ///         Challenge
        ///     </seealso>
        ///     .
        /// </summary>
        public const string CodeChallengeMethod = "code_challenge_method";

        /// <summary>
        ///     OAuth response_mode parameter, per
        ///     <seealso href="https://openid.net/specs/oauth-v2-multiple-response-types-1_0.html">
        ///         OAuth 2.0 Multiple Response Type Encoding Practices §2.1
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ResponseMode = "response_mode";

        /// <summary>
        ///     OAuth token_type response parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.4">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §4.1.4: Access Token Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string TokenType = "token_type";

        /// <summary>
        ///     OpenID Connect max_age parameter, per
        ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
        ///         OpenID Connect Core 1.0 §3.1.2.1:
        ///         Authentication Request
        ///     </seealso>
        ///     .
        /// </summary>
        public const string MaxAge = "max_age";

        /// <summary>
        ///     OAuth error_uri parameter, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §5.2: Error Response
        ///     </seealso>
        ///     .
        /// </summary>
        public const string ErrorUri = "error_uri";
    }

    #endregion

    #region Nested type: PermissionPrefixes

    /// <summary>
    ///     Prefix strings used to namespace permission entries stored on clients.
    /// </summary>
    public static class PermissionPrefixes
    {
        /// <summary>Prefix for grant type permission entries.</summary>
        public const string GrantType = "g:";

        /// <summary>Prefix for scope permission entries.</summary>
        public const string Scope = "s:";

        /// <summary>Prefix for endpoint permission entries.</summary>
        public const string Endpoint = "e:";
    }

    #endregion

    #region Nested type: PkceMethods

    /// <summary>
    ///     PKCE (Proof Key for Code Exchange) code challenge method identifiers, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html">
    ///         RFC 7636: Proof Key for Code Exchange by OAuth Public
    ///         Clients
    ///     </seealso>
    ///     .
    /// </summary>
    public static class PkceMethods
    {
        /// <summary>SHA-256 code challenge method.</summary>
        public const string S256 = "S256";

        /// <summary>Plain code challenge method.</summary>
        public const string Plain = "plain";
    }

    #endregion

    #region Nested type: PreconditionSubjects

    /// <summary>
    ///     Well-known precondition subjects.
    /// </summary>
    public static class PreconditionSubjects
    {
        /// <summary>The request itself is the subject.</summary>
        public const string Request = "request";
    }

    #endregion

    #region Nested type: PromptValues

    /// <summary>
    ///     OpenID Connect prompt parameter values, per
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
    ///         OpenID Connect Core 1.0 §3.1.2.1:
    ///         Authentication Request
    ///     </seealso>
    ///     .
    /// </summary>
    public static class PromptValues
    {
        /// <summary>No authentication or consent UI should be displayed.</summary>
        public const string None = "none";

        /// <summary>The authorization server should prompt for re-authentication.</summary>
        public const string Login = "login";

        /// <summary>The authorization server should prompt for consent.</summary>
        public const string Consent = "consent";

        /// <summary>The authorization server should prompt for account selection.</summary>
        public const string SelectAccount = "select_account";
    }

    #endregion

    #region Nested type: Properties

    /// <summary>
    ///     Serialization property names used for authorization and token metadata.
    /// </summary>
    public static class Properties
    {
        public const string GrantType           = ".grant_type";
        public const string Scope               = ".scope";
        public const string IssuedTokenType     = ".issued_token_type";
        public const string ResponseType        = ".response_type";
        public const string Nonce               = ".nonce";
        public const string RedirectUri         = ".redirect_uri";
        public const string ResponseMode        = ".response_mode";
        public const string State               = ".state";
        public const string CodeChallenge       = ".code_challenge";
        public const string CodeChallengeMethod = ".code_challenge_method";
        public const string AuthorizationName   = ".authorization_name";
        public const string SessionId           = ".session_id";
        public const string MaxAge              = ".max_age";
        public const string AuthTime            = ".auth_time";
    }

    #endregion

    #region Nested type: ResponseModes

    /// <summary>
    ///     OAuth 2.0 response mode values, per
    ///     <seealso href="https://openid.net/specs/oauth-v2-multiple-response-types-1_0.html">
    ///         OAuth 2.0 Multiple Response Type Encoding Practices §2.1
    ///     </seealso>
    ///     and
    ///     <seealso href="https://openid.net/specs/oauth-v2-form-post-response-mode-1_0.html">
    ///         OAuth 2.0 Form Post Response Mode
    ///     </seealso>
    ///     .
    /// </summary>
    public static class ResponseModes
    {
        /// <summary>Response parameters are appended to the redirect URI query component.</summary>
        public const string Query = "query";

        /// <summary>Response parameters are appended to the redirect URI fragment component.</summary>
        public const string Fragment = "fragment";

        /// <summary>Response parameters are sent via HTTP POST to the redirect URI.</summary>
        public const string FormPost = "form_post";
    }

    #endregion

    #region Nested type: ResponseTypes

    /// <summary>
    ///     OAuth 2.0 response type values, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.1.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.1.1: Response Type
    ///     </seealso>
    ///     .
    /// </summary>
    public static class ResponseTypes
    {
        /// <summary>Authorization code response type.</summary>
        public const string Code = "code";

        /// <summary>Access token response type (implicit / hybrid flow).</summary>
        public const string Token = "token";

        /// <summary>ID token response type (implicit / hybrid flow).</summary>
        public const string IdToken = "id_token";
    }

    #endregion

    #region Nested type: Schemes

    /// <summary>
    ///     OAuth 2.0 access token type identifiers, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-7.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §7.1: Access Token Types
    ///     </seealso>
    ///     .
    /// </summary>
    public static class Schemes
    {
        /// <summary>
        ///     Bearer token type, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6750.html">
        ///         RFC 6750: The OAuth 2.0 Authorization Framework:
        ///         Bearer Token Usage
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Bearer = "Bearer";

        /// <summary>Basic scheme type.</summary>
        public const string Basic = "Basic";
    }

    #endregion

    #region Nested type: Scopes

    /// <summary>
    ///     Well-known OAuth 2.0 / OpenID Connect scope identifiers.
    /// </summary>
    public static class Scopes
    {
        /// <summary>
        ///     OpenID Connect scope; requests an ID token, per
        ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
        ///         OpenID Connect Core 1.0 §3.1.2.1:
        ///         Authentication Request
        ///     </seealso>
        ///     .
        /// </summary>
        public const string OpenId = "openid";

        /// <summary>Requests access to the end-user's default profile claims.</summary>
        public const string Profile = "profile";

        /// <summary>Requests access to the end-user's role claims.</summary>
        public const string Role = "role";

        /// <summary>Requests access to the end-user's email address.</summary>
        public const string Email = "email";

        /// <summary>Requests access to the end-user's phone number.</summary>
        public const string Phone = "phone";

        /// <summary>Requests access to the end-user's physical mailing address.</summary>
        public const string Address = "address";

        /// <summary>
        ///     Requests a refresh token for offline access, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.3">
        ///         RFC 6749: The OAuth 2.0 Authorization
        ///         Framework §3.3: Access Token Scope
        ///     </seealso>
        ///     .
        /// </summary>
        public const string OfflineAccess = "offline_access";
    }

    #endregion

    #region Nested type: SigningAlgorithms

    /// <summary>
    ///     JWS signing algorithm identifiers, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.1">
    ///         RFC 7518: JSON Web Algorithms (JWA) §3.1: "alg" (Algorithm) Header Parameter Values for JWS
    ///     </seealso>
    ///     .
    /// </summary>
    public static class SigningAlgorithms
    {
        /// <summary>
        ///     RSASSA-PKCS1-v1_5 using SHA-256, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.3">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.3:
        ///         Digital Signature with RSASSA-PKCS1-v1_5
        ///     </seealso>
        ///     .
        /// </summary>
        public const string RsaSha256 = "RS256";

        /// <summary>
        ///     RSASSA-PKCS1-v1_5 using SHA-384, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.3">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.3:
        ///         Digital Signature with RSASSA-PKCS1-v1_5
        ///     </seealso>
        ///     .
        /// </summary>
        public const string RsaSha384 = "RS384";

        /// <summary>
        ///     RSASSA-PKCS1-v1_5 using SHA-512, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.3">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.3:
        ///         Digital Signature with RSASSA-PKCS1-v1_5
        ///     </seealso>
        ///     .
        /// </summary>
        public const string RsaSha512 = "RS512";

        /// <summary>
        ///     ECDSA using P-256 and SHA-256, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.4">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.4:
        ///         Digital Signature with ECDSA
        ///     </seealso>
        ///     .
        /// </summary>
        public const string EcdsaSha256 = "ES256";

        /// <summary>
        ///     ECDSA using P-384 and SHA-384, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.4">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.4:
        ///         Digital Signature with ECDSA
        ///     </seealso>
        ///     .
        /// </summary>
        public const string EcdsaSha384 = "ES384";

        /// <summary>
        ///     ECDSA using P-521 and SHA-512, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.4">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.4:
        ///         Digital Signature with ECDSA
        ///     </seealso>
        ///     .
        /// </summary>
        public const string EcdsaSha512 = "ES512";

        /// <summary>
        ///     RSASSA-PSS using SHA-256 and MGF1 with SHA-256, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.5">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.5:
        ///         Digital Signature with RSASSA-PSS
        ///     </seealso>
        ///     .
        /// </summary>
        public const string RsaPssSha256 = "PS256";

        /// <summary>
        ///     RSASSA-PSS using SHA-384 and MGF1 with SHA-384, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.5">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.5:
        ///         Digital Signature with RSASSA-PSS
        ///     </seealso>
        ///     .
        /// </summary>
        public const string RsaPssSha384 = "PS384";

        /// <summary>
        ///     RSASSA-PSS using SHA-512 and MGF1 with SHA-512, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.5">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.5:
        ///         Digital Signature with RSASSA-PSS
        ///     </seealso>
        ///     .
        /// </summary>
        public const string RsaPssSha512 = "PS512";

        /// <summary>
        ///     HMAC using SHA-256, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.2">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.2:
        ///         HMAC with SHA-2 Functions
        ///     </seealso>
        ///     .
        /// </summary>
        public const string HmacSha256 = "HS256";

        /// <summary>
        ///     HMAC using SHA-384, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.2">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.2:
        ///         HMAC with SHA-2 Functions
        ///     </seealso>
        ///     .
        /// </summary>
        public const string HmacSha384 = "HS384";

        /// <summary>
        ///     HMAC using SHA-512, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7518.html#section-3.2">
        ///         RFC 7518: JSON Web Algorithms (JWA) §3.2:
        ///         HMAC with SHA-2 Functions
        ///     </seealso>
        ///     .
        /// </summary>
        public const string HmacSha512 = "HS512";
    }

    #endregion

    #region Nested type: StandardScopes

    /// <summary>
    ///     OIDC scope-to-claims mapping, per
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#ScopeClaims">OpenID Connect Core 1.0 §5.4</seealso>
    ///     .
    /// </summary>
    public static class StandardScopes
    {
        public static readonly IReadOnlyDictionary<string, string[]> ScopeClaims = new Dictionary<string, string[]> {
            [Scopes.Profile] = [
                Claims.Name, Claims.FamilyName, Claims.GivenName, Claims.MiddleName, Claims.Nickname,
                Claims.PreferredUsername, Claims.Profile, Claims.Picture, Claims.Website, Claims.Gender,
                Claims.Birthdate, Claims.Zoneinfo, Claims.Locale, Claims.UpdatedAt,
            ],
            [Scopes.Role]    = [Claims.Role],
            [Scopes.Email]   = [Claims.Email, Claims.EmailVerified],
            [Scopes.Phone]   = [Claims.PhoneNumber, Claims.PhoneNumberVerified],
            [Scopes.Address] = [Claims.Address],
        };
    }

    #endregion

    #region Nested type: SubjectTypes

    /// <summary>
    ///     OpenID Connect subject identifier type identifiers.
    /// </summary>
    public static class SubjectTypes
    {
        /// <summary>A public subject identifier, the same for all clients.</summary>
        public const string Public = "public";

        /// <summary>A pairwise subject identifier, unique per client.</summary>
        public const string Pairwise = "pairwise";
    }

    #endregion

    #region Nested type: TokenFormats

    /// <summary>
    ///     Token serialization format identifiers.
    /// </summary>
    public static class TokenFormats
    {
        /// <summary>
        ///     JSON Web Token, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7519.html">RFC 7519: JSON Web Token (JWT)</seealso>.
        /// </summary>
        public const string Jwt = "jwt";

        /// <summary>
        ///     JSON Web Encryption, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7516.html">RFC 7516: JSON Web Encryption (JWE)</seealso>.
        /// </summary>
        public const string Jwe = "jwe";

        /// <summary>Opaque reference that must be introspected.</summary>
        public const string Reference = "reference";
    }

    #endregion

    #region Nested type: TokenStatuses

    /// <summary>
    ///     Well-known token status values.
    /// </summary>
    public static class TokenStatuses
    {
        /// <summary>The token is valid and has not been revoked or redeemed.</summary>
        public const string Valid = "valid";

        /// <summary>The token has been revoked.</summary>
        public const string Revoked = "revoked";

        /// <summary>The token has been redeemed (e.g., an authorization code that was exchanged).</summary>
        public const string Redeemed = "redeemed";

        /// <summary>The token represents an authorized but not yet completed request.</summary>
        public const string Authorized = "authorized";

        /// <summary>The token has been denied by the user.</summary>
        public const string Denied = "denied";
    }

    #endregion

    #region Nested type: TokenTypes

    /// <summary>
    ///     OAuth 2.0 / OpenID Connect token type identifiers.
    /// </summary>
    public static class TokenTypes
    {
        /// <summary>An authorization code.</summary>
        public const string AuthorizationCode = "authorization_code";

        /// <summary>An access token.</summary>
        public const string AccessToken = "access_token";

        /// <summary>A refresh token.</summary>
        public const string RefreshToken = "refresh_token";

        /// <summary>An OpenID Connect ID token.</summary>
        public const string IdToken = "id_token";

        /// <summary>
        ///     A device code, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html">RFC 8628: OAuth 2.0 Device Authorization Grant</seealso>
        ///     .
        /// </summary>
        public const string DeviceCode = TokenTypeUris.DeviceCode;

        /// <summary>An interaction token used during authorization UI flows.</summary>
        public const string Interaction = TokenTypeUris.Interaction;

        /// <summary>
        ///     A user code for device authorization, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html">RFC 8628: OAuth 2.0 Device Authorization Grant</seealso>
        ///     .
        /// </summary>
        public const string UserCode = TokenTypeUris.UserCode;

        /// <summary>A logout token used during RP-initiated logout with front-channel notifications.</summary>
        public const string Logout = TokenTypeUris.Logout;
    }

    #endregion

    #region Nested type: TokenTypeUris

    /// <summary>
    ///     URN token type identifiers used in token exchange and internal flows.
    /// </summary>
    public static class TokenTypeUris
    {
        /// <summary>
        ///     JWT token type URI, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html#section-3">
        ///         RFC 8693: OAuth 2.0 Token Exchange §3: Token
        ///         Type Identifiers
        ///     </seealso>
        ///     .
        /// </summary>
        public const string Jwt = "urn:ietf:params:oauth:token-type:jwt";

        /// <summary>
        ///     Access token type URI, per
        ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html#section-3">
        ///         RFC 8693: OAuth 2.0 Token Exchange §3: Token
        ///         Type Identifiers
        ///     </seealso>
        ///     .
        /// </summary>
        public const string AccessToken = "urn:ietf:params:oauth:token-type:access_token";

        /// <summary>Schemata-internal token type URI for an interaction token reference.</summary>
        public const string Interaction = "urn:schemata:authorization:token-type:interaction";

        /// <summary>Schemata-internal token type URI for a device code.</summary>
        public const string DeviceCode = "urn:schemata:authorization:token-type:device-code";

        /// <summary>Schemata-internal token type URI for a device user code.</summary>
        public const string UserCode = "urn:schemata:authorization:token-type:user-code";

        /// <summary>Schemata-internal token type URI for a logout interaction token.</summary>
        public const string Logout = "urn:schemata:authorization:token-type:logout";
    }

    #endregion
}
