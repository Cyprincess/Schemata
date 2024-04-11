using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[DisplayName("Role")]
[Table("SchemataRoles")]
[CanonicalName("roles/{role}")]
public class SchemataRole : IdentityRole<long>, IIdentifier, ICanonicalName, IDisplayName, IConcurrency, ITimestamp
{
    [NotMapped]
    public override string? ConcurrencyStamp
    {
        get => Timestamp?.ToString();
        set => Timestamp = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid() : Guid.Parse(value);
    }

    #region ICanonicalName Members

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IDisplayName Members

    public virtual string? DisplayName { get; set; }

    public virtual string? DisplayNames { get; set; }

    #endregion

    #region IIdentifier Members

    [Key]
    public override long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreationDate { get; set; }

    public virtual DateTime? ModificationDate { get; set; }

    #endregion
}
