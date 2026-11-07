using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Quota.Skeleton.Entities;

[Table("SchemataQuotaReservations")]
[PrimaryKey(nameof(Scope), nameof(Subject), nameof(OperationId))]
[Index(nameof(Uid))]
[Index(nameof(State), nameof(ExpireTime))]
public class SchemataQuotaReservation : IIdentifier, IConcurrency, ITimestamp, IExpiration
{
    public virtual Guid Uid { get; set; }
    public virtual string Scope { get; set; } = string.Empty;
    public virtual string Subject { get; set; } = string.Empty;
    public virtual string OperationId { get; set; } = string.Empty;
    public virtual string PayloadFingerprint { get; set; } = string.Empty;
    public virtual QuotaReservationState State { get; set; }
    public virtual DateTime? ExpireTime { get; set; }

    [NotMapped]
    public virtual List<SchemataQuotaReservationItem> Items { get; set; } = [];

    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    public virtual DateTime? CreateTime { get; set; }
    public virtual DateTime? UpdateTime { get; set; }
}
