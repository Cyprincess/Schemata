using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("Users")]
[CanonicalName("users/{user}")]
public class SchemataUser : IdentityUser<long>, IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    [NotMapped]
    public override string? ConcurrencyStamp
    {
        get => Timestamp?.ToString();
        set => Timestamp = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid() : Guid.Parse(value);
    }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreationDate { get; set; }

    public virtual DateTime? ModificationDate { get; set; }

    #endregion
}
