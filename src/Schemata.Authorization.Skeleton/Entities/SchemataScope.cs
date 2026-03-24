using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

/// <summary>
///     Represents an OAuth 2.0 scope that defines a permission boundary for access tokens.
/// </summary>
/// <remarks>
///     Scopes control the extent of access granted to a client application.
///     Standard scopes include "openid", "profile", "email"; custom scopes
///     map to API resources.
/// </remarks>
[DisplayName("Scope")]
[Table("SchemataScopes")]
[CanonicalName("scopes/{scope}")]
public class SchemataScope : IIdentifier, ICanonicalName, IDisplayName, IConcurrency, ITimestamp
{
    /// <summary>Gets or sets the scope description shown on consent screens.</summary>
    public virtual string? Description { get; set; }

    /// <summary>Gets or sets the JSON-serialized localized descriptions.</summary>
    public virtual string? Descriptions { get; set; }

    /// <summary>Gets or sets the JSON-serialized custom properties.</summary>
    public virtual string? Properties { get; set; }

    /// <summary>Gets or sets the JSON-serialized API resources associated with this scope.</summary>
    public virtual string? Resources { get; set; }

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

    #region IDisplayName Members

    /// <inheritdoc />
    public virtual string? DisplayName { get; set; }

    /// <summary>Gets or sets the JSON-serialized localized display names.</summary>
    public virtual string? DisplayNames { get; set; }

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
