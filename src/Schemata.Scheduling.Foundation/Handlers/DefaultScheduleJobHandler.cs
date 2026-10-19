using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Foundation.Settlement;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Abstractions.Advisors;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class DefaultScheduleJobHandler<TJob, TExecution>(SchedulingHandlerSupport<TJob, TExecution> support, IServiceProvider services)
    : IRequestHandler<ScheduleJobRequest<TJob>, Unit>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public async Task<Unit> HandleAsync(ScheduleJobRequest<TJob> request, AdviceContext context, CancellationToken ct = default) {
        request.Job.MessageContext = new((await MessageContexts.CaptureAsync(services, ct)).Items);
        request.Job.ScheduleVersion = Guid.NewGuid();
        if (request.ReplaceVariables) {
            request.Job.Variables = request.Variables is null
                ? null
                : new Dictionary<string, string?>(request.Variables);
        }

        await ScheduleCoreAsync(request.Job, ct, null);
        return Unit.Value;
    }

    internal async Task ScheduleCoreAsync(TJob job, CancellationToken ct, Guid? recoveryVersion) {
        var scheduler = support.Scheduler;
        await support.WriteGate.Gate.WaitAsync(ct);
        try {
            var context = new MessageContext(job.MessageContext ?? new());
            var scope = await scheduler.Services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(context, ct);
            IDisposable? entered = null;
            Exception? failure = null;
            try {
            entered = scope.Enter();
            await scope.RestoreAsync(context, ct);
            var jobs = scope.Services.GetRequiredService<IRepository<TJob>>();
            var executions = scope.Services.GetRequiredService<IRepository<TExecution>>();
            await using var transaction = jobs.Begin();
            executions.Join(transaction);
            var name = job.CanonicalName ?? job.Name;
            var persisted = job.Key is { } slot
                ? await jobs.FirstOrDefaultAsync(query => query.Where(row => row.Key == slot), ct)
                : string.IsNullOrWhiteSpace(name)
                    ? null
                    : await jobs.FirstOrDefaultAsync(
                        query => query.Where(row => row.CanonicalName == name || row.Name == name), ct);
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

                if (key is not null && scheduler.Entries.TryGetValue(key, out var existing)) {
                    replayedMisses = existing.ReplayedMisses;
                }

                var now = scheduler.Time.GetUtcNow().UtcDateTime;
                if (job.NextRunTime is { } next) {
                    var options = scheduler.Options.Value;
                    job.NextRunTime = MissedFireWindow.Resolve(
                        job, next, now, options.MissedFirePolicy, options.MaxMissedWalk, replayedMisses);
                }
            } finally {
                scheduler.Gate.Release();
            }

            var mutation = scope.Services.GetRequiredService<IResourceMutation<TJob>>();
            if (persisted is null) {
                if (await mutation.CreateAsync(job, transaction, ct) != MutationResult.Applied) return;
            } else {
                persisted.ScheduleVersion = job.ScheduleVersion;
                // Execution result fields belong to the settlement owner.
                persisted.Key          ??= job.Key;
                persisted.JobKey         = job.JobKey;
                persisted.ArgsJson       = job.ArgsJson;
                persisted.ScheduleType   = job.ScheduleType;
                persisted.NextRunTime    = job.NextRunTime;
                persisted.IntervalTicks  = job.IntervalTicks;
                persisted.AnchorTime     = job.AnchorTime;
                persisted.CronExpression = job.CronExpression;
                persisted.Variables      = job.Variables;
                persisted.MessageContext = job.MessageContext;
                persisted.Replay         = job.Replay;
                persisted.State          = job.State;
                if (await mutation.UpdateAsync(persisted, transaction, ct: ct) != MutationResult.Applied) return;
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

            await support.ReplacePendingExecutionAsync(job, executions, scope.Services, transaction, ct);
            await transaction.CommitAsync(ct);
            await support.ArmOneShotTimerAsync(job, replayedMisses);
            } catch (Exception error) {
                failure = error;
                throw;
            } finally {
                Exception? cleanup = null;
                try { await scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
                try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
                if (cleanup is not null) {
                    if (failure is not null) throw new AggregateException(failure, cleanup);
                    ExceptionDispatchInfo.Capture(cleanup).Throw();
                }
            }
        } finally {
            support.WriteGate.Gate.Release();
        }

        await support.NotifyScheduledAsync(job, ct);
    }

}
