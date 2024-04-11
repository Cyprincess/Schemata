using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataRoleClaims")]
public class SchemataRoleClaim : IdentityRoleClaim<long>, IIdentifier, ITimestamp
{
    #region IIdentifier Members

    public new virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreationDate { get; set; }

    public virtual DateTime? ModificationDate { get; set; }

    #endregion
}
