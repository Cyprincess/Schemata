using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataUserLogins")]
public class SchemataUserLogin : IdentityUserLogin<long>, ITimestamp
{
    [Key]
    public override string LoginProvider { get; set; } = null!;

    [Key]
    public override string ProviderKey { get; set; } = null!;

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
