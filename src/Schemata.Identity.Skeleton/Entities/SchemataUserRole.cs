using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataUserRole")]
public class SchemataUserRole : IdentityUserRole<long>, ITimestamp
{
    /// <inheritdoc />
    public override long UserId { get; set; }

    /// <inheritdoc />
    public override long RoleId { get; set; }

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
