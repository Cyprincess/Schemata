using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

/// <summary>
///     Authentication token entity for Schemata identity users.
/// </summary>
[Table("SchemataUserTokens")]
[PrimaryKey(nameof(UserId), nameof(LoginProvider), nameof(Name))]
public class SchemataUserToken : IdentityUserToken<string>, ITimestamp
{
    public override string UserId { get; set; } = null!;

    public override string LoginProvider { get; set; } = null!;

    public override string Name { get; set; } = null!;

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
