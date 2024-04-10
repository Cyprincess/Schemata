using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("UserRole")]
public class SchemataUserRole : IdentityUserRole<long>, ITimestamp
{
    #region ITimestamp Members

    public virtual DateTime? CreationDate { get; set; }

    public virtual DateTime? ModificationDate { get; set; }

    #endregion
}
