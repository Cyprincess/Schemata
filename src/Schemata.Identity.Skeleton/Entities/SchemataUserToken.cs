using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("SchemataUserTokens")]
public class SchemataUserToken : IdentityUserToken<long>, ITimestamp
{
    public override long UserId { get; set; }

    public override string LoginProvider { get; set; } = null!;

    public override string Name { get; set; } = null!;
    
    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
