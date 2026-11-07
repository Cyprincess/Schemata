using System;
using System.Collections.Generic;

namespace Schemata.Quota.Skeleton;

public sealed record QuotaReservationView(
    Guid Uid,
    QuotaScope Scope,
    QuotaSubject Subject,
    string OperationId,
    QuotaReservationState State,
    DateTimeOffset? ExpireTime,
    IReadOnlyList<QuotaAccountView> Accounts,
    TimeSpan? RetryAfter);
