using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Authorization.Skeleton.Entities;

/// <summary>
///     Represents a resource owner's consent grant to an application for a set of scopes.
/// </summary>
[DisplayName("Authorization")]
[Table("SchemataAuthorizations")]
[CanonicalName("authorizations/{authorization}")]
public class SchemataAuthorization : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    /// <summary>The application that received this authorization.</summary>
    public virtual string? ApplicationName { get; set; }

    /// <summary>Identifier of the resource owner who granted this authorization.</summary>
    public virtual string? Subject { get; set; }

    /// <summary>
    ///     Distinguishes consent records so the consent advisor can apply the correct reuse policy.
    ///     <c>"ad-hoc"</c> records (authorization-code flow) may be matched on subsequent /authorize calls;
    ///     <c>"device"</c> records are explicitly skipped so a device approval cannot silently authorize
    ///     a browser /authorize on the same client;
    ///     <c>"permanent"</c> records represent persistent cross-session consent.
    /// </summary>
    public virtual string? Type { get; set; }

    /// <summary>Lifecycle status, e.g. <c>"valid"</c> or <c>"revoked"</c>.</summary>
    public virtual string? Status { get; set; }

    /// <summary>Space-delimited scopes granted in this authorization.</summary>
    public virtual string? Scopes { get; set; }

    /// <summary>The redirect URI associated with this authorization.</summary>
    public virtual string? RedirectUri { get; set; }

    /// <summary>The response type associated with this authorization.</summary>
    public virtual string? ResponseType { get; set; }

    /// <summary>The PKCE code challenge method associated with this authorization.</summary>
    public virtual string? CodeChallengeMethod { get; set; }

    /// <summary>The requested Authentication Context Class References associated with this authorization.</summary>
    public virtual string? AcrValues { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    [TableKey]
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
