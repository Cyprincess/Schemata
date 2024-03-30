using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Foundation.Entities;

[Table("Users")]
[CanonicalName("users/{user}")]
public class SchemataUser : IdentityUser<long>, IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    public override string? ConcurrencyStamp
    {
        get => Timestamp?.ToString();
        set => Timestamp = Guid.TryParse(value, out var timestamp) ? timestamp : Guid.NewGuid();
    }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public Guid? Timestamp { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreationDate { get; set; }

    public DateTime? ModificationDate { get; set; }

    #endregion
}
