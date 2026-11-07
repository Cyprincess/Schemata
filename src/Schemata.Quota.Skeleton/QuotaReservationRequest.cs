using System;
using System.Collections.Generic;

namespace Schemata.Quota.Skeleton;

public sealed record QuotaReservationRequest(
    QuotaScope? Scope,
    QuotaSubject Subject,
    string OperationId,
    IReadOnlyList<QuotaCharge> Charges,
    DateTimeOffset? ExpiresAt);
