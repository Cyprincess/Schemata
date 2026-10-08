using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

/// <summary>
///     Link entity between Schemata identity users and roles.
/// </summary>
[Table("SchemataUserRole")]
[PrimaryKey(nameof(UserId), nameof(RoleId))]
public class SchemataUserRole : IdentityUserRole<string>, ITimestamp
{
    public override string UserId { get; set; } = null!;

    public override string RoleId { get; set; } = null!;

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
