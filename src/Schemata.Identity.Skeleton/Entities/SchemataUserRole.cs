using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataUserRole")]
[PrimaryKey(nameof(UserId), nameof(RoleId))]
public class SchemataUserRole : IdentityUserRole<Guid>, ITimestamp
{
    public override Guid UserId { get; set; }

    public override Guid RoleId { get; set; }

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
