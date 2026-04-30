using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using static Schemata.Abstractions.SchemataConstants;

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
[DisplayName("Application")]
[Table("SchemataApplications")]
[CanonicalName("applications/{application}")]
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
    ///     Hashed client secret for confidential clients.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.3.1: Client Password
    ///     </seealso>
    /// </summary>
    public virtual string? ClientSecret { get; set; }

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

    /// <summary>When non-null, overrides the global PKCE requirement for this application.</summary>
    public virtual bool? RequirePkce { get; set; }

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

    /// <summary>When non-null, overrides the global subject type for this application.</summary>
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

    #region ICanonicalName Members

    public virtual string? Name
    {
        get => ClientId;
        set => ClientId = value;
    }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IDescriptive Members

    public virtual string? DisplayName { get; set; }

    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    public virtual string? Description { get; set; }

    public virtual Dictionary<string, string>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
