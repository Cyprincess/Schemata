using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[DisplayName("Role")]
[Table("SchemataRoles")]
[CanonicalName("roles/{role}")]
public class SchemataRole : IdentityRole<long>, IIdentifier, ICanonicalName, IDescriptive, IConcurrency, ITimestamp
{
    /// <summary>Bridges Identity's string-based ConcurrencyStamp to the Guid-based Timestamp.</summary>
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
