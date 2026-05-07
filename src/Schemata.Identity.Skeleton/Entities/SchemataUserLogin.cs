using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataUserLogins")]
public class SchemataUserLogin : IdentityUserLogin<Guid>, ITimestamp
{
    /// <inheritdoc />
    [TableKey(0)]
    public override string LoginProvider { get; set; } = null!;

    /// <inheritdoc />
    [TableKey(1)]
    public override string ProviderKey { get; set; } = null!;

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
