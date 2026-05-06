using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataUserRole")]
public class SchemataUserRole : IdentityUserRole<Guid>, ITimestamp
{
    [TableKey(0)]
    public override Guid UserId { get; set; }

    [TableKey(1)]
    public override Guid RoleId { get; set; }

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
