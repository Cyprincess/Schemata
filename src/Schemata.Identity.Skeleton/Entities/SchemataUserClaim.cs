using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

/// <summary>
///     Identity user claim entity with Schemata identifiers and timestamps.
/// </summary>
[Table("SchemataUserClaims")]
[PrimaryKey(nameof(Uid))]
public class SchemataUserClaim : IdentityUserClaim<Guid>, IIdentifier, ITimestamp
{
    #region IIdentifier Members
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
