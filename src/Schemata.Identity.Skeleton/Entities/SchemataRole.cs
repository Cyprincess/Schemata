using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("Roles")]
[CanonicalName("roles/{role}")]
public class SchemataRole : IdentityRole<long>, IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    public string CanonicalName { get; set; }

    [NotMapped]
    public override string ConcurrencyStamp
    {
        get => Timestamp?.ToString();
        set => Timestamp = Guid.Parse(value);
    }

    public virtual Guid? Timestamp { get; set; }

    public DateTime? CreationDate { get; set; }

    public DateTime? ModificationDate { get; set; }
}
