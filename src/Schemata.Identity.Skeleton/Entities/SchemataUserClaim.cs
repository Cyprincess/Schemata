using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Schemata.Identity.Skeleton.Entities;

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
