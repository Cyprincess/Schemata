using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Skeleton.Entities;

[DisplayName("Application")]
[Table("SchemataApplications")]
[CanonicalName("applications/{application}")]
public class SchemataApplication : IIdentifier, ICanonicalName, IDescriptive, IConcurrency, ITimestamp
{
    /// <summary>Alias for <see cref="Name" />.</summary>
    public virtual string? ClientId
    {
        get => Name;
        set => Name = value;
    }

    /// <summary>Hashed client secret for confidential clients per RFC 6749 section 2.3.1.</summary>
    public virtual string? ClientSecret { get; set; }

    /// <summary>OAuth 2.0 client type: "confidential" or "public" per RFC 6749 section 2.1.</summary>
    public virtual string? ClientType { get; set; } = ClientTypes.Confidential;

    /// <summary>Application type: "web" or "native" per OpenID Connect Dynamic Registration.</summary>
    public virtual string? ApplicationType { get; set; } = ApplicationTypes.Web;

    /// <summary>Consent model: "explicit", "implicit", or "external" controlling the consent prompt.</summary>
    public virtual string? ConsentType { get; set; } = ConsentTypes.Explicit;

    /// <summary>When null, falls back to global authorization options.</summary>
    public virtual bool? RequirePkce { get; set; }

    /// <summary>Registered redirect URIs for authorization code and implicit flows per RFC 6749 section 3.1.2.</summary>
    public virtual ICollection<string>? RedirectUris { get; set; }

    /// <summary>Granted endpoint and grant-type permissions (e.g. "ept:token", "gt:authorization_code").</summary>
    public virtual ICollection<string>? Permissions { get; set; }

    /// <summary>Allowed post-logout redirect URIs per OpenID Connect RP-Initiated Logout.</summary>
    public virtual ICollection<string>? PostLogoutRedirectUris { get; set; }

    /// <summary>When null, falls back to global configuration.</summary>
    public virtual string? SubjectType { get; set; }

    /// <summary>Used for pairwise subject identifiers.</summary>
    public virtual string? SectorIdentifierUri { get; set; }

    /// <summary>frontchannel_logout_uri per OpenID Connect Front-Channel Logout §2. Presence implies support.</summary>
    public virtual string? FrontChannelLogoutUri { get; set; }

    /// <summary>frontchannel_logout_session_required per OpenID Connect Front-Channel Logout §2.</summary>
    public virtual bool FrontChannelLogoutSessionRequired { get; set; }

    /// <summary>backchannel_logout_uri per OpenID Connect Back-Channel Logout §2.2. Presence implies support.</summary>
    public virtual string? BackChannelLogoutUri { get; set; }

    /// <summary>backchannel_logout_session_required per OpenID Connect Back-Channel Logout §2.2.</summary>
    public virtual bool BackChannelLogoutSessionRequired { get; set; }

    #region ICanonicalName Members

    /// <inheritdoc />
    public virtual string? Name { get; set; }

    /// <inheritdoc />
    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    /// <inheritdoc />
    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IDescriptive Members

    /// <inheritdoc />
    public virtual string? DisplayName { get; set; }

    /// <inheritdoc />
    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    /// <inheritdoc />
    public virtual string? Description { get; set; }

    /// <inheritdoc />
    public virtual Dictionary<string, string>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members

    /// <inheritdoc />
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
