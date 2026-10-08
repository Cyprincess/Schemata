using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Tenancy;
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
        request.Job.Tenant = TenantContext.Current.Uid?.ToString("D") ?? "host";
        request.Job.ScheduleVersion = Guid.NewGuid();
        if (request.ReplaceVariables) {
            request.Job.Variables = request.Variables is null
                ? null
                : new Dictionary<string, string?>(request.Variables);
        }

        await ScheduleCoreAsync(request.Job, ct, null);
        return Unit.Value;
    }

    internal async Task ScheduleCoreAsync(SchemataJob job, CancellationToken ct, Guid? recoveryVersion) {
        var scheduler = support.Scheduler;
        await support.WriteGate.Gate.WaitAsync(ct);
        try {
            using var scope = scheduler.Services.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
            var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            await using var transaction = jobs.Begin();
            executions.Join(transaction);
            var name = job.CanonicalName ?? job.Name;
            var persisted = job.Key is { } slot
                ? await jobs.FirstOrDefaultAsync(query => query.Where(row => row.Tenant == job.Tenant && row.Key == slot), ct)
                : string.IsNullOrWhiteSpace(name)
                    ? null
                    : await jobs.FirstOrDefaultAsync(
                        query => query.Where(row => row.Tenant == job.Tenant && (row.CanonicalName == name || row.Name == name)), ct);
            if (recoveryVersion is { } version
                && (persisted is null || persisted.State != JobState.Active || persisted.ScheduleVersion != version)) {
                return;
            }
            var key = persisted?.CanonicalName ?? name;
            var replayedMisses = 0;
            await scheduler.Gate.WaitAsync(ct);
            try {
                if (scheduler.IsStopped) {
                    return;
                }

                if (key is not null && scheduler.Entries.TryRemove(key, out var existing)) {
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

            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataJob>>();
            if (persisted is null) {
                await mutation.CreateAsync(job, transaction, ct);
            } else {
                persisted.ScheduleVersion = job.ScheduleVersion;
                // Execution result fields belong to the staging handler.
                persisted.Key          ??= job.Key;
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
                await mutation.UpdateAsync(persisted, transaction, ct: ct);
                job.Uid           = persisted.Uid;
                job.Name          = persisted.Name;
                job.CanonicalName = persisted.CanonicalName;
                // The rotated stamp is final only at the provider's write boundary; the preparation
                // projects it onto the caller-held job after rotation, and rollback restores the
                // staged value so a failed commit never surfaces an unpersisted stamp.
                var staged = job.Timestamp;
                transaction.AddSavePreparation(() => job.Timestamp = persisted.Timestamp);
                transaction.AddRollbackSink(() => job.Timestamp = staged);
            }

            await support.ReplacePendingExecutionAsync(job, executions, scope.ServiceProvider, transaction, ct);
            await transaction.CommitAsync(ct);
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
