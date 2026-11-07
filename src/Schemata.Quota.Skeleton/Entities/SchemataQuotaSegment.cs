using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Quota.Skeleton.Entities;

[Table("SchemataQuotaSegments")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(AccountUid))]
[Index(nameof(ExpireTime))]
public class SchemataQuotaSegment : IIdentifier
{
    public virtual Guid Uid { get; set; }
    public virtual Guid AccountUid { get; set; }
    public virtual DateTimeOffset StartTime { get; set; }
    public virtual DateTimeOffset ExpireTime { get; set; }
    public virtual long Committed { get; set; }
    public virtual long Reserved { get; set; }
}
