using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataUserLogins")]
public class SchemataUserLogin : IdentityUserLogin<long>, ITimestamp
{
    /// <inheritdoc />
    public override string LoginProvider { get; set; } = null!;

    /// <inheritdoc />
    public override string ProviderKey { get; set; } = null!;

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
