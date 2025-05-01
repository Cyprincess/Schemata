using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataUserRole")]
public class SchemataUserRole : IdentityUserRole<long>, ITimestamp
{
    [Key]
    public override long UserId { get; set; }

    [Key]
    public override long RoleId { get; set; }

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
