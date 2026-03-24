using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

/// <summary>
///     Represents an OAuth 2.0 / OpenID Connect authorization grant.
/// </summary>
/// <remarks>
///     An authorization binds a subject (user) to a client application with a set of scopes.
///     Permanent authorizations persist across token requests so that returning users are not
///     prompted for consent again.
/// </remarks>
[DisplayName("Authorization")]
[Table("SchemataAuthorizations")]
[CanonicalName("authorizations/{authorization}")]
public class SchemataAuthorization : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    /// <summary>Gets or sets the foreign key to the associated <see cref="SchemataApplication" />.</summary>
    public virtual long? ApplicationId { get; set; }

    /// <summary>Gets or sets the subject (user identifier) the authorization was granted to.</summary>
    public virtual string? Subject { get; set; }

    /// <summary>Gets or sets the authorization type (e.g. "permanent", "ad-hoc").</summary>
    public virtual string? Type { get; set; }

    /// <summary>Gets or sets the current status (e.g. "valid", "revoked").</summary>
    public virtual string? Status { get; set; }

    /// <summary>Gets or sets the JSON-serialized custom properties.</summary>
    public virtual string? Properties { get; set; }

    /// <summary>Gets or sets the JSON-serialized scopes associated with this authorization.</summary>
    public virtual string? Scopes { get; set; }

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

    #region IIdentifier Members

    /// <inheritdoc />
    [Key]
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
