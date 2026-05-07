using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataRoleClaims")]
public class SchemataRoleClaim : IdentityRoleClaim<Guid>, IIdentifier, ITimestamp
{
    #region IIdentifier Members

    /// <inheritdoc />
    [TableKey]
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
