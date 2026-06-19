using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

/// <summary>
///     External login entity for Schemata identity users.
/// </summary>
[Table("SchemataUserLogins")]
[PrimaryKey(nameof(LoginProvider), nameof(ProviderKey))]
public class SchemataUserLogin : IdentityUserLogin<Guid>, ITimestamp
{
    public override string LoginProvider { get; set; } = null!;

    public override string ProviderKey { get; set; } = null!;

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
