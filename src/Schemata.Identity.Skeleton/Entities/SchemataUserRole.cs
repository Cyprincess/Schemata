using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataUserRole")]
public class SchemataUserRole : IdentityUserRole<Guid>, ITimestamp
{
    /// <inheritdoc />
    [TableKey(0)]
    public override Guid UserId { get; set; }

    /// <inheritdoc />
    [TableKey(1)]
    public override Guid RoleId { get; set; }

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
