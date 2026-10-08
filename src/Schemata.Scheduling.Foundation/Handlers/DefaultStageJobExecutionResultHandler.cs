using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class DefaultStageJobExecutionResultHandler(SchedulingHandlerSupport support)
    : IRequestHandler<StageJobExecutionResultRequest, Unit>
{
    public async Task<Unit> HandleAsync(StageJobExecutionResultRequest request, CancellationToken ct = default) {
        var scheduler = support.Scheduler;
        SchemataJob? rearmed = null;

        await support.WriteGate.Gate.WaitAsync(ct);
        try {
            using var scope = scheduler.Services.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
            var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            await using var transaction = jobs.Begin();
            executions.Join(transaction);
            var persisted = await jobs.FirstOrDefaultAsync(
                query => query.Where(job => job.CanonicalName == request.JobCanonicalName
                                          || job.Name == request.JobCanonicalName), ct);
            if (persisted is null || persisted.ScheduleVersion != request.ScheduleVersion) {
                return Unit.Value;
            }

            var replayed = scheduler.Entries.TryGetValue(request.JobCanonicalName, out var current)
                ? current.ReplayedMisses
                : 0;
            var next = request.NextRunTime;
            var now  = scheduler.Time.GetUtcNow().UtcDateTime;
            if (scheduler.Options.Value.MissedFirePolicy == MissedFirePolicy.FireAll
             && next is { } due && due <= now
             && replayed >= scheduler.Options.Value.MaxMissedWalk - 1) {
                next = support.AdvancePastMissedWindow(persisted, due, now);
            }

            persisted.State         = request.State;
            persisted.RecentRunTime = request.RecentRunTime;
            persisted.RecentError   = request.RecentError;
            persisted.NextRunTime   = next;
            await scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataJob>>().UpdateAsync(persisted, transaction, ct: ct);
            if (request.State == JobState.Active && next is not null) {
                await support.ReplacePendingExecutionAsync(persisted, executions, scope.ServiceProvider, transaction, ct);
            }
            await transaction.CommitAsync(ct);

            if (request.State == JobState.Active && next is not null) {
                await support.ArmOneShotTimerAsync(persisted, replayed);
                rearmed = persisted;
            }
        } finally {
            support.WriteGate.Gate.Release();
        }

        if (rearmed is not null) {
            await support.NotifyScheduledAsync(rearmed, ct);
        }

        return Unit.Value;
    }
}
