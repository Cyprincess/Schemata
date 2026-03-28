using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

[DisplayName("Authorization")]
[Table("SchemataAuthorizations")]
[CanonicalName("authorizations/{authorization}")]
public class SchemataAuthorization : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    public virtual string? ApplicationName { get; set; }

    /// <summary>Identifier of the resource owner who granted this authorization.</summary>
    public virtual string? Subject { get; set; }

    /// <summary>
    ///     Authorization type — see <c>SchemataConstants.AuthorizationTypes</c>. Distinguishes
    ///     consent-reusable records from single-grant anchors:
    ///     <para>
    ///         <c>"ad-hoc"</c> — authorization-code flow consent. The consent advisor may match it
    ///         on subsequent /authorize calls with the same subject + client + ⊇ scope, allowing silent consent.
    ///     </para>
    ///     <para>
    ///         <c>"device"</c> — device flow approval. The consent advisor explicitly skips records of this
    ///         type so a device approval cannot silently authorize a subsequent browser /authorize on the
    ///         same client (the verifying user agent is not the requesting device).
    ///     </para>
    ///     <para>
    ///         <c>"permanent"</c> — explicitly-stored persistent consent record reusable across sessions.
    ///     </para>
    /// </summary>
    public virtual string? Type { get; set; }

    /// <summary>Current status, e.g. "valid" or "revoked".</summary>
    public virtual string? Status { get; set; }

    /// <summary>Space-delimited scopes granted in this authorization.</summary>
    public virtual string? Scopes { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    /// <inheritdoc />
    public virtual Guid? Timestamp { get; set; }

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
