using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Quota.Skeleton.Entities;

[Table("SchemataQuotaAccounts")]
[PrimaryKey(nameof(Scope), nameof(Subject), nameof(Metric), nameof(PolicyName), nameof(PolicyVersion))]
[Index(nameof(Uid))]
public class SchemataQuotaAccount : IIdentifier, IConcurrency, ITimestamp
{
    public virtual Guid Uid { get; set; }
    public virtual string Scope { get; set; } = string.Empty;
    public virtual string Subject { get; set; } = string.Empty;
    public virtual string Metric { get; set; } = string.Empty;
    public virtual string PolicyName { get; set; } = string.Empty;
    public virtual int PolicyVersion { get; set; }
    public virtual long Committed { get; set; }
    public virtual long Reserved { get; set; }
    public virtual decimal Available { get; set; }
    public virtual DateTimeOffset WindowStart { get; set; }

    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    public virtual DateTime? CreateTime { get; set; }
    public virtual DateTime? UpdateTime { get; set; }
}
