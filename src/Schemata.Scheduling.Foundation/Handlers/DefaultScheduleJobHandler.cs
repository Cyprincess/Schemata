using System;
using System.Collections.Generic;
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

internal sealed class DefaultScheduleJobHandler(SchedulingHandlerSupport support)
    : IRequestHandler<ScheduleJobRequest, Unit>
{
    public async Task<Unit> HandleAsync(ScheduleJobRequest request, CancellationToken ct = default) {
        if (request.ReplaceVariables) {
            request.Job.Variables = request.Variables is null
                ? null
                : new Dictionary<string, string?>(request.Variables);
        }

        await ScheduleCoreAsync(request.Job, ct);
        return Unit.Value;
    }

    private async Task ScheduleCoreAsync(SchemataJob job, CancellationToken ct) {
        var scheduler = support.Scheduler;
        var key       = job.CanonicalName ?? job.Name;
        if (string.IsNullOrWhiteSpace(key)) {
            return;
        }

        await support.WriteGate.Gate.WaitAsync(ct);
        try {
            var replayedMisses = 0;
            await scheduler.Gate.WaitAsync(ct);
            try {
                if (scheduler.IsStopped) {
                    return;
                }

                if (scheduler.Entries.TryRemove(key, out var existing)) {
                    replayedMisses = existing.ReplayedMisses;
                    await existing.Cts.CancelAsync();
                    existing.Cts.Dispose();
                }

                if (!job.NextRunTime.HasValue) {
                    return;
                }

                var now = scheduler.Time.GetUtcNow().UtcDateTime;
                if (scheduler.Options.Value.MissedFirePolicy == MissedFirePolicy.FireAll
                 && job.NextRunTime <= now
                 && replayedMisses >= scheduler.Options.Value.MaxMissedWalk - 1) {
                    job.NextRunTime = support.AdvancePastMissedWindow(job, job.NextRunTime.Value, now);
                } else {
                    job.NextRunTime = AdjustForMissedWindow(job, now);
                }
            } finally {
                scheduler.Gate.Release();
            }

            using (var scope = scheduler.Services.CreateScope()) {
                var jobs = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
                var persisted = await jobs.FirstOrDefaultAsync(
                    query => query.Where(row => row.CanonicalName == key || row.Name == key), ct);
                if (persisted is null) {
                    await jobs.AddAsync(job, ct);
                } else {
                    // A reschedule rewrites the scheduling configuration and the requested State; the result fields
                    // (RecentRunTime/RecentError) belong to the staging handler.
                    persisted.JobKey         = job.JobKey;
                    persisted.ArgsJson       = job.ArgsJson;
                    persisted.ScheduleType   = job.ScheduleType;
                    persisted.NextRunTime    = job.NextRunTime;
                    persisted.IntervalTicks  = job.IntervalTicks;
                    persisted.AnchorTime     = job.AnchorTime;
                    persisted.CronExpression = job.CronExpression;
                    persisted.Variables      = job.Variables;
                    persisted.Replay         = job.Replay;
                    persisted.State          = job.State;
                    await jobs.UpdateAsync(persisted, ct);
                }

                await jobs.CommitAsync(ct);
            }

            await support.EnsurePendingExecutionAsync(job, ct);

            await support.ArmOneShotTimerAsync(job, replayedMisses);
        } finally {
            support.WriteGate.Gate.Release();
        }

        await support.NotifyScheduledAsync(job, ct);
    }

    private DateTime AdjustForMissedWindow(SchemataJob job, DateTime now) {
        var scheduler = support.Scheduler;
        var next      = job.NextRunTime.GetValueOrDefault();
        if (next > now || !job.Replay || job.ScheduleType is not (ScheduleType.Cron or ScheduleType.Periodic)) {
            return next;
        }

        switch (scheduler.Options.Value.MissedFirePolicy) {
            case MissedFirePolicy.Skip:
                for (var i = 0; i < scheduler.Options.Value.MaxMissedWalk && next <= now; i++) {
                    var advanced = SchedulingHandlerSupport.ComputeAfter(job, next);
                    if (advanced <= next) {
                        break;
                    }

                    next = advanced;
                }

                return next;

            case MissedFirePolicy.FireOnce:
                for (var i = 0; i < scheduler.Options.Value.MaxMissedWalk; i++) {
                    var probe = SchedulingHandlerSupport.ComputeAfter(job, next);
                    if (probe > now || probe <= next) {
                        break;
                    }

                    next = probe;
                }

                return next;

            case MissedFirePolicy.FireAll:
            default:
                return next;
        }
    }
}
