using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Skeleton.Entities;

/// <summary>
///     Represents an OAuth 2.0 client or OpenID Connect Relying Party registered with the authorization server,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §2: Client Registration
///     </seealso>
///     .
/// </summary>
[Table("SchemataApplications")]
[CanonicalName("applications/{application}")]
[PrimaryKey(nameof(Uid))]
public class SchemataApplication : IIdentifier, ICanonicalName, IDescriptive, IConcurrency, ITimestamp
{
    /// <summary>
    ///     OAuth 2.0 client identifier.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.2: Client Identifier
    ///     </seealso>
    /// </summary>
    public virtual string? ClientId { get; set; }

    /// <summary>
    ///     <c>token_endpoint_auth_method</c>, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    /// <remarks><see langword="null" /> on legacy rows leaves the authentication channel unconstrained.</remarks>
    public virtual string? TokenEndpointAuthMethod { get; set; }

    /// <summary>
    ///     <c>token_endpoint_auth_signing_alg</c>, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual string? TokenEndpointAuthSigningAlg { get; set; }

    /// <summary>
    ///     OAuth 2.0 client type: <c>"confidential"</c> or <c>"public"</c>.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.1: Client Types
    ///     </seealso>
    /// </summary>
    public virtual string? ClientType { get; set; } = ClientTypes.Confidential;

    /// <summary>Application type: <c>"web"</c> or <c>"native"</c>.</summary>
    public virtual string? ApplicationType { get; set; } = ApplicationTypes.Web;

    /// <summary>Consent model: <c>"explicit"</c>, <c>"implicit"</c>, or <c>"external"</c>.</summary>
    public virtual string? ConsentType { get; set; } = ConsentTypes.Explicit;

    /// <summary>Application-specific PKCE requirement override.</summary>
    public virtual bool? RequirePkce { get; set; }

    /// <summary>
    ///     <c>dpop_bound_access_tokens</c> — whether the client always uses DPoP for token
    ///     requests; when <see langword="true" />, token requests without a DPoP proof are
    ///     rejected, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-5.2">
    ///         RFC 9449: OAuth 2.0 Demonstrating Proof
    ///         of Possession (DPoP) §5.2: Client Registration
    ///         Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual bool DpopBoundAccessTokens { get; set; }

    /// <summary>
    ///     Registered redirect URIs.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.1.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.1.2: Redirection Endpoint
    ///     </seealso>
    /// </summary>
    public virtual ICollection<string>? RedirectUris { get; set; }

    /// <summary>Granted permissions, e.g. <c>"ept:token"</c>, <c>"gt:authorization_code"</c>.</summary>
    public virtual ICollection<string>? Permissions { get; set; }

    /// <summary>Allowed post-logout redirect URIs for RP-Initiated Logout.</summary>
    public virtual ICollection<string>? PostLogoutRedirectUris { get; set; }

    /// <summary>Application-specific subject identifier type override.</summary>
    public virtual string? SubjectType { get; set; }

    /// <summary>Required for pairwise subject identifiers to scope the hash.</summary>
    public virtual string? SectorIdentifierUri { get; set; }

    /// <summary>
    ///     <c>frontchannel_logout_uri</c>. Presence implies support for front-channel logout.
    /// </summary>
    public virtual string? FrontChannelLogoutUri { get; set; }

    /// <summary><c>frontchannel_logout_session_required</c>.</summary>
    public virtual bool FrontChannelLogoutSessionRequired { get; set; }

    /// <summary>
    ///     <c>backchannel_logout_uri</c>. Presence implies support for back-channel logout.
    /// </summary>
    public virtual string? BackChannelLogoutUri { get; set; }

    /// <summary><c>backchannel_logout_session_required</c>.</summary>
    public virtual bool BackChannelLogoutSessionRequired { get; set; }

    /// <summary>
    ///     <c>contacts</c> — e-mail addresses of people responsible for the client, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual ICollection<string>? Contacts { get; set; }

    /// <summary>
    ///     <c>logo_uri</c> — URL referencing the client's logo image, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual string? LogoUri { get; set; }

    /// <summary>
    ///     <c>client_uri</c> — URL of the client's home page, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual string? ClientUri { get; set; }

    /// <summary>
    ///     <c>policy_uri</c> — URL of the client's policy on how it uses profile data, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual string? PolicyUri { get; set; }

    /// <summary>
    ///     <c>tos_uri</c> — URL of the client's terms of service, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual string? TosUri { get; set; }

    /// <summary>
    ///     <c>require_auth_time</c> — whether the <c>auth_time</c> claim is required in ID Tokens
    ///     issued to the client, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual bool RequireAuthTime { get; set; }

    /// <summary>
    ///     <c>default_max_age</c> — default maximum authentication age in integer seconds, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    /// <remarks>The wire form is integer seconds; the value is carried as a string to align with
    ///     <see cref="Schemata.Authorization.Skeleton.Models.AuthorizeRequest.MaxAge" />.</remarks>
    public virtual string? DefaultMaxAge { get; set; }

    /// <summary>
    ///     <c>default_acr_values</c> — default Authentication Context Class Reference values
    ///     requested by the client, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual ICollection<string>? DefaultAcrValues { get; set; }

    /// <summary>
    ///     <c>initiate_login_uri</c> — URI a third party can use to initiate a login by the client,
    ///     per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual string? InitiateLoginUri { get; set; }

    /// <summary>
    ///     <c>software_id</c> — unique identifier assigned by the client developer to the client
    ///     software, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7591.html#section-2">
    ///         RFC 7591: OAuth 2.0 Dynamic Client
    ///         Registration Protocol §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual string? SoftwareId { get; set; }

    /// <summary>
    ///     <c>software_version</c> — version identifier for the client software identified by
    ///     <see cref="SoftwareId" />, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7591.html#section-2">
    ///         RFC 7591: OAuth 2.0 Dynamic Client
    ///         Registration Protocol §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual string? SoftwareVersion { get; set; }

    /// <summary>
    ///     <c>software_statement</c> — signed software statement JWT asserting client metadata, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7591.html#section-2.3">
    ///         RFC 7591: OAuth 2.0 Dynamic Client
    ///         Registration Protocol §2.3: Software Statement
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual string? SoftwareStatement { get; set; }

    /// <summary>
    ///     <c>authorization_details_types</c> — authorization details types the client will use in
    ///     <c>authorization_details</c> objects, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html#section-10">
    ///         RFC 9396: OAuth 2.0 Rich Authorization
    ///         Requests §10: Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    public virtual ICollection<string>? AuthorizationDetailsTypes { get; set; }

    #region ICanonicalName Members

    public virtual string? Name
    {
        get => ClientId;
        set => ClientId = value;
    }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    #endregion

    #region IDescriptive Members

    /// <summary>
    ///     <c>client_name</c>, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
    ///         OpenID Connect Dynamic Client
    ///         Registration 1.0 §2: Client Metadata
    ///     </seealso>
    ///     .
    /// </summary>
    /// <remarks>The <see cref="DisplayNames" /> dictionary carries the <c>client_name#lang</c> variants.</remarks>
    public virtual string? ClientName { get; set; }

    string? IDescriptive.DisplayName {
        get => ClientName;
        set => ClientName = value;
    }

    public virtual Dictionary<string, string?>? DisplayNames { get; set; }

    public virtual string? Description { get; set; }

    public virtual Dictionary<string, string?>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
