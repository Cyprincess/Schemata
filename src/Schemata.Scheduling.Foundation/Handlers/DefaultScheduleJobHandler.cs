using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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

        await ScheduleCoreAsync(request.Job, ct, null, request.UnitOfWork);
        return Unit.Value;
    }

    internal async Task ScheduleCoreAsync(TJob job, CancellationToken ct, Guid? recoveryVersion, IUnitOfWork? outer = null) {
        var scheduler = support.Scheduler;
        bool scheduled;
        await support.WriteGate.Gate.WaitAsync(ct);
        try {
            var context = new MessageContext(job.MessageContext ?? new());
            var runner = scheduler.Services.GetRequiredService<IMessageExecutionScopeRunner>();
            Func<IServiceProvider, CancellationToken, ValueTask<bool>> work = async (provider, token) => {
            var jobs = provider.GetRequiredService<IRepository<TJob>>();
            var executions = provider.GetRequiredService<IRepository<TExecution>>();
            IUnitOfWork transaction;
            IUnitOfWork? owned = null;
            if (outer is not null) {
                transaction = outer;
                jobs.Join(outer);
            } else {
                owned = jobs.Begin(job.Key is null ? null : IsolationLevel.Serializable);
                transaction = owned;
            }
            await using var ownedTransaction = owned;
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
                return false;
            }
            var key = persisted?.CanonicalName ?? name;
            var replayedMisses = 0;
            await scheduler.Gate.WaitAsync(ct);
            try {
                if (scheduler.IsStopped) {
                    return false;
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

            var mutation = provider.GetRequiredService<IResourceMutation<TJob>>();
            if (persisted is null) {
                if (await mutation.CreateAsync(job, transaction, ct) != MutationResult.Applied) return false;
            } else {
                persisted.ScheduleVersion = job.ScheduleVersion;
                // Execution result fields belong to the settlement owner.
                persisted.Key          ??= job.Key;
                persisted.JobKey         = job.JobKey;
                persisted.ArgsJson       = job.ArgsJson;
                persisted.Subject        = job.Subject;
                persisted.ScheduleType   = job.ScheduleType;
                persisted.NextRunTime    = job.NextRunTime;
                persisted.IntervalTicks  = job.IntervalTicks;
                persisted.AnchorTime     = job.AnchorTime;
                persisted.CronExpression = job.CronExpression;
                persisted.Variables      = job.Variables;
                persisted.MessageContext = job.MessageContext;
                persisted.Replay         = job.Replay;
                persisted.State          = job.State;
                if (await mutation.UpdateAsync(persisted, transaction, ct: ct) != MutationResult.Applied) return false;
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

            await support.ReplacePendingExecutionAsync(job, executions, provider, transaction, token);
            if (ownedTransaction is not null) {
                await transaction.CommitAsync(token);
                await support.ArmOneShotTimerAsync(job, replayedMisses);
            } else {
                transaction.AddCommitSink(CommitOrders.Domain, _ => support.ArmOneShotTimerAsync(job, replayedMisses));
                transaction.AddCommitSink(CommitOrders.Domain, token => support.NotifyScheduledAsync(job, token));
            }
            return true;
            };
            scheduled = outer is not null
                ? await work(services, ct)
                : await runner.RunAsync(context, work, ct);
        } finally {
            support.WriteGate.Gate.Release();
        }

        if (scheduled && outer is null) await support.NotifyScheduledAsync(job, ct);
    }

}
