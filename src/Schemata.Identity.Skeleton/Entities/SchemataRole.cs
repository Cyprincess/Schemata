using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

/// <summary>
///     Represents a role in the Schemata identity system.
/// </summary>
/// <remarks>
///     Extends ASP.NET Core <see cref="IdentityRole{TKey}"/> with Schemata entity interfaces
///     for canonical naming, display names, optimistic concurrency, and audit timestamps.
///     Stored in the <c>SchemataRoles</c> table with a <see langword="long"/> primary key.
/// </remarks>
[DisplayName("Role")]
[Table("SchemataRoles")]
[CanonicalName("roles/{role}")]
public class SchemataRole : IdentityRole<long>, IIdentifier, ICanonicalName, IDisplayName, IConcurrency, ITimestamp
{
    /// <summary>
    ///     Gets or sets the concurrency stamp, backed by the <see cref="Timestamp"/> GUID.
    /// </summary>
    [NotMapped]
    public override string? ConcurrencyStamp
    {
        get => Timestamp?.ToString();
        set => Timestamp = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid() : Guid.Parse(value);
    }

    #region ICanonicalName Members

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

    /// <inheritdoc />
    public virtual string? DisplayNames { get; set; }

    #endregion

    #region IIdentifier Members

    /// <inheritdoc />
    [Key]
    public override long Id { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
