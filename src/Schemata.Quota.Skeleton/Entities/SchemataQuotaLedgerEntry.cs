using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Quota.Skeleton.Entities;

[Table("SchemataQuotaLedgerEntries")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(ReservationUid))]
[Index(nameof(AccountUid))]
[Index(nameof(OperationId))]
public class SchemataQuotaLedgerEntry : IIdentifier
{
    public virtual Guid Uid { get; set; }
    public virtual Guid ReservationUid { get; set; }
    public virtual Guid AccountUid { get; set; }
    public virtual string OperationId { get; set; } = string.Empty;
    public virtual QuotaSettlementKind Kind { get; set; }
    public virtual long Amount { get; set; }
    public virtual DateTime? CreateTime { get; set; }
}
