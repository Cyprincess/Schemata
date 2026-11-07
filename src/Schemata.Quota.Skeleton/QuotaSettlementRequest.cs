using System;

namespace Schemata.Quota.Skeleton;

public sealed record QuotaSettlementRequest(QuotaScope? Scope, QuotaSubject Subject, Guid ReservationUid);
