using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Successful registration response, per
///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
///         OpenID Connect Dynamic Client Registration 1.0 §3.2.1: Client Registration Response
///     </seealso>
///     .
/// </summary>
public sealed class RegistrationResponse
{
    /// <summary><c>client_id</c> (REQUIRED). OAuth 2.0 client identifier.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    ///     <c>client_secret</c>. Present only for confidential clients with a secret-based
    ///     authentication method; returned once, never again.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary><c>client_id_issued_at</c>. Unix seconds.</summary>
    public long? ClientIdIssuedAt { get; set; }

    /// <summary>
    ///     <c>client_secret_expires_at</c>. Unix seconds; <c>0</c> means the secret does not expire.
    ///     REQUIRED when <see cref="ClientSecret" /> is present.
    /// </summary>
    public long? ClientSecretExpiresAt { get; set; }

    /// <summary>
    ///     <c>registration_access_token</c>. Bearer token authorizing GET access to the client's
    ///     registration record. Issued together with <see cref="RegistrationClientUri" /> or not at all.
    /// </summary>
    public string? RegistrationAccessToken { get; set; }

    /// <summary>
    ///     <c>registration_client_uri</c>. Per-client registration read-back endpoint.
    ///     Issued together with <see cref="RegistrationAccessToken" /> or not at all.
    /// </summary>
    public string? RegistrationClientUri { get; set; }

    /// <summary><c>redirect_uris</c>.</summary>
    public List<string>? RedirectUris { get; set; }

    /// <summary><c>token_endpoint_auth_method</c>.</summary>
    public string? TokenEndpointAuthMethod { get; set; }

    /// <summary><c>grant_types</c>.</summary>
    public List<string>? GrantTypes { get; set; }

    /// <summary><c>response_types</c>.</summary>
    public List<string>? ResponseTypes { get; set; }

    /// <summary><c>client_name</c>.</summary>
    public string? ClientName { get; set; }

    /// <summary><c>client_uri</c>.</summary>
    public string? ClientUri { get; set; }

    /// <summary><c>logo_uri</c>.</summary>
    public string? LogoUri { get; set; }

    /// <summary><c>scope</c>.</summary>
    public string? Scope { get; set; }


    /// <summary><c>token_endpoint_auth_signing_alg</c>.</summary>
    public string? TokenEndpointAuthSigningAlg { get; set; }

    /// <summary><c>contacts</c>.</summary>
    public List<string>? Contacts { get; set; }

    /// <summary><c>tos_uri</c>.</summary>
    public string? TosUri { get; set; }

    /// <summary><c>policy_uri</c>.</summary>
    public string? PolicyUri { get; set; }

    /// <summary><c>jwks_uri</c>.</summary>
    public string? JwksUri { get; set; }

    /// <summary><c>jwks</c>.</summary>
    public string? Jwks { get; set; }

    /// <summary><c>application_type</c>.</summary>
    public string? ApplicationType { get; set; }

    /// <summary><c>sector_identifier_uri</c>.</summary>
    public string? SectorIdentifierUri { get; set; }

    /// <summary><c>subject_type</c>.</summary>
    public string? SubjectType { get; set; }

    /// <summary><c>default_max_age</c>.</summary>
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

    /// <summary><c>software_id</c>.</summary>
    public string? SoftwareId { get; set; }

    /// <summary><c>software_version</c>.</summary>
    public string? SoftwareVersion { get; set; }
}
