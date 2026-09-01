using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class DefaultUnscheduleJobHandler(SchedulingHandlerSupport support)
    : IRequestHandler<UnscheduleJobRequest, Unit>
{
    public async Task<Unit> HandleAsync(UnscheduleJobRequest request, CancellationToken ct = default) {
        var scheduler = support.Scheduler;
        SchemataJob? notified = null;

        await support.WriteGate.Gate.WaitAsync(ct);
        try {
            await scheduler.Gate.WaitAsync(ct);
            try {
                if (scheduler.Entries.TryRemove(request.JobCanonicalName, out var entry)) {
                    notified = entry.Job;
                    notified.State = JobState.Paused;
                    await entry.Cts.CancelAsync();
                    entry.Cts.Dispose();
                }
            } finally {
                scheduler.Gate.Release();
            }

            await support.CancelFuturePendingAsync(request.JobCanonicalName, ct);

            using var scope = scheduler.Services.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
            var persisted = await jobs.FirstOrDefaultAsync(
                query => query.Where(job => job.CanonicalName == request.JobCanonicalName
                                          || job.Name == request.JobCanonicalName), ct);
            if (persisted is not null) {
                persisted.State = JobState.Paused;
                await jobs.UpdateAsync(persisted, ct);
                await jobs.CommitAsync(ct);
                notified ??= persisted;
            }
        } finally {
            support.WriteGate.Gate.Release();
        }

        if (notified is not null) {
            await support.NotifyUnscheduledAsync(notified, ct);
        }

        return Unit.Value;
    }
}
