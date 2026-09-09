using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Client metadata submitted to the dynamic registration endpoint, per
///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
///         OpenID Connect Dynamic Client Registration 1.0 §2: Client Metadata
///     </seealso>
///     .
/// </summary>
public class RegisterRequest
{
    /// <summary><c>redirect_uris</c> (REQUIRED). Absolute URIs the client may redirect to.</summary>
    public List<string>? RedirectUris { get; set; }

    /// <summary><c>token_endpoint_auth_method</c>. Defaults to <c>client_secret_basic</c>.</summary>
    public string? TokenEndpointAuthMethod { get; set; }

    /// <summary><c>grant_types</c>. Defaults to <c>["authorization_code"]</c>.</summary>
    public List<string>? GrantTypes { get; set; }

    /// <summary><c>response_types</c>. Defaults to <c>["code"]</c>.</summary>
    public List<string>? ResponseTypes { get; set; }

    /// <summary><c>client_name</c>. Human-readable string.</summary>
    public string? ClientName { get; set; }

    /// <summary><c>client_uri</c>.</summary>
    public string? ClientUri { get; set; }

    /// <summary><c>logo_uri</c>.</summary>
    public string? LogoUri { get; set; }

    /// <summary><c>scope</c>. Space-delimited; mapped to the client permission set.</summary>
    public string? Scope { get; set; }

    /// <summary><c>contacts</c>. E-mail addresses of people responsible for the client.</summary>
    public List<string>? Contacts { get; set; }

    /// <summary><c>tos_uri</c>.</summary>
    public string? TosUri { get; set; }

    /// <summary><c>policy_uri</c>.</summary>
    public string? PolicyUri { get; set; }

    /// <summary><c>jwks_uri</c>. URL for the client's JSON Web Key Set. Mutually exclusive with <see cref="Jwks" />.</summary>
    public string? JwksUri { get; set; }

    /// <summary><c>jwks</c>. Client's JSON Web Key Set document, embedded. Mutually exclusive with <see cref="JwksUri" />.</summary>
    public string? Jwks { get; set; }

    /// <summary><c>application_type</c>. <c>web</c> (default) or <c>native</c>.</summary>
    public string? ApplicationType { get; set; }

    /// <summary><c>sector_identifier_uri</c>. For pairwise subject identifiers.</summary>
    public string? SectorIdentifierUri { get; set; }

    /// <summary><c>subject_type</c>. <c>public</c> or <c>pairwise</c>.</summary>
    public string? SubjectType { get; set; }

    /// <summary><c>id_token_signed_response_alg</c>.</summary>
    public string? IdTokenSignedResponseAlg { get; set; }

    /// <summary><c>userinfo_signed_response_alg</c>. JWS algorithm signing the UserInfo response.</summary>
    public string? UserinfoSignedResponseAlg { get; set; }

    /// <summary><c>userinfo_encrypted_response_alg</c>. JWE key management algorithm (client's public key).</summary>
    public string? UserinfoEncryptedResponseAlg { get; set; }

    /// <summary><c>userinfo_encrypted_response_enc</c>. JWE content encryption algorithm.</summary>
    public string? UserinfoEncryptedResponseEnc { get; set; }

    /// <summary><c>token_endpoint_auth_signing_alg</c>.</summary>
    public string? TokenEndpointAuthSigningAlg { get; set; }

    /// <summary><c>default_max_age</c>. Integer seconds.</summary>
    public string? DefaultMaxAge { get; set; }

    /// <summary><c>require_auth_time</c>.</summary>
    public bool? RequireAuthTime { get; set; }

    /// <summary><c>default_acr_values</c>.</summary>
    public List<string>? DefaultAcrValues { get; set; }

    /// <summary><c>initiate_login_uri</c>.</summary>
    public string? InitiateLoginUri { get; set; }

    /// <summary><c>post_logout_redirect_uris</c>.</summary>
    public List<string>? PostLogoutRedirectUris { get; set; }

    /// <summary><c>frontchannel_logout_uri</c>.</summary>
    public string? FrontChannelLogoutUri { get; set; }

    /// <summary><c>frontchannel_logout_session_required</c>.</summary>
    public bool? FrontChannelLogoutSessionRequired { get; set; }

    /// <summary><c>backchannel_logout_uri</c>.</summary>
    public string? BackChannelLogoutUri { get; set; }

    /// <summary><c>backchannel_logout_session_required</c>.</summary>
    public bool? BackChannelLogoutSessionRequired { get; set; }

    /// <summary><c>software_id</c>. Unique identifier assigned by the client developer.</summary>
    public string? SoftwareId { get; set; }

    /// <summary><c>software_version</c>.</summary>
    public string? SoftwareVersion { get; set; }

    /// <summary><c>software_statement</c>. Signed software statement JWT, stored verbatim.</summary>
    public string? SoftwareStatement { get; set; }

    /// <summary>
    ///     <c>require_pushed_authorization_requests</c>, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9126.html#section-5.1">
    ///         RFC 9126: OAuth 2.0 Pushed Authorization Requests §5.1: Indicating Server Support
    ///     </seealso>
    ///     . <see langword="true" /> binds the client to PAR for every authorization request.
    /// </summary>
    public bool? RequirePushedAuthorizationRequests { get; set; }

    /// <summary>
    ///     <c>request_object_signing_alg</c>, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9101.html#section-3">
    ///         RFC 9101: JWT-Secured Authorization Request (JAR) §3: Request Object Registration
    ///     </seealso>
    ///     . The single JWS algorithm the client uses to sign request objects;
    ///     <see langword="null" /> leaves the choice open to the server.
    /// </summary>
    public string? RequestObjectSigningAlg { get; set; }

    /// <summary>
    ///     <c>require_signed_request_object</c>, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9101.html#section-10.5">
    ///         RFC 9101 §10.5: Choice of Algorithms
    ///     </seealso>
    ///     . When <see langword="true" /> the client refuses unsigned request objects.
    /// </summary>
    public bool? RequireSignedRequestObject { get; set; }
}
