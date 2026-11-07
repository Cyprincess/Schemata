using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Quota.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Quota.Foundation.Jobs;

[ScheduledJob(QuotaConstants.Jobs.Expiry)]
public sealed class QuotaExpiryJob(IQuotaAccounting accounting) : IScheduledJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct) {
        if (!context.Variables.TryGetValue(QuotaConstants.Jobs.ReservationUidVariable, out var value)
         || !Guid.TryParse(value, out var reservationUid)) {
            throw new InvalidOperationException("The quota expiry trigger requires a reservation UID.");
        }

        return accounting.ExpireAsync(reservationUid, ct: ct).AsTask();
    }
}
