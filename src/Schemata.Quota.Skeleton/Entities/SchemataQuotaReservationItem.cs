using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Quota.Skeleton.Entities;

[Table("SchemataQuotaReservationItems")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(ReservationUid))]
[Index(nameof(AccountUid))]
public class SchemataQuotaReservationItem : IIdentifier
{
    public virtual Guid Uid { get; set; }
    public virtual Guid ReservationUid { get; set; }
    public virtual Guid AccountUid { get; set; }
    public virtual string Metric { get; set; } = string.Empty;
    public virtual string PolicyName { get; set; } = string.Empty;
    public virtual int PolicyVersion { get; set; }
    public virtual long Amount { get; set; }
    public virtual string Algorithm { get; set; } = string.Empty;
    public virtual long Limit { get; set; }
    public virtual long PeriodTicks { get; set; }
    public virtual long Burst { get; set; }
    public virtual DateTimeOffset WindowStart { get; set; }

    public QuotaPolicy ToPolicy() {
        return new(PolicyName, PolicyVersion, Algorithm, Limit, TimeSpan.FromTicks(PeriodTicks), Burst);
    }
}
