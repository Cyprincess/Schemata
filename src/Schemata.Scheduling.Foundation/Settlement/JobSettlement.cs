using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Handlers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Settlement;

internal sealed class JobSettlement<TJob, TExecution> : IJobSettlement<TJob, TExecution>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    #region IJobSettlement Members

    public async Task<SettlementOutcome> SettleAsync(
        JobExecutionLease<TJob, TExecution> lease,
        JobExecutionResult<TJob>            result,
        CancellationToken                   ct = default
    ) {
        var state = await lease.TransitionAsync(
            (services, store, token) => SettleCoreAsync(lease, services, store, lease.Execution, result, token), ct);

        return state.Outcome;
    }

    #endregion

    private static async Task<SettlementState> SettleCoreAsync(
        JobExecutionLease<TJob, TExecution>  lease,
        IServiceProvider                     services,
        IJobExecutionStore<TJob, TExecution> store,
        TExecution                           execution,
        JobExecutionResult<TJob>             result,
        CancellationToken                    ct
    ) {
        var support = services.GetRequiredService<SchedulingHandlerSupport<TJob, TExecution>>();
        var jobs    = services.GetService<IRepository<TJob>>();
        if (jobs is null) {
            Project(result.Job, result, support.Scheduler.Time.GetUtcNow().UtcDateTime, support, 0);
            var settled = await SettleExecutionAsync(store, execution, result, ct);

            return new(new SettlementOutcome(settled, true, null), null, 0);
        }

        var executions = services.GetService<IRepository<TExecution>>();
        var state      = default(SettlementState);

        await support.WriteGate.Gate.WaitAsync(ct);
        try {
            if (executions is not null && await TryBeginSharedAsync(jobs, executions) is { } unit) {
                await using (unit) {
                    state = await SettleAtomicAsync(lease, services, jobs, executions, unit, execution, result, support, ct);
                }
            } else {
                // A rejected join completes the scope's job repository, so the schedule stage runs
                // in a fresh scope; the untouched execution store settles the claimed row.
                state = await lease.RecoverAsync(
                    (fresh, _, token) => SettleSequentialAsync(fresh, store, execution, result, token), ct);
            }

            if (state.Rearmed is { } rearmed) {
                await support.ArmOneShotTimerAsync(rearmed, state.Replayed);
            }
        } finally {
            support.WriteGate.Gate.Release();
        }

        if (state.Rearmed is not null) {
            await support.NotifyScheduledAsync(state.Rearmed, ct);
        }

        return state;
    }

    // Both stores share a provider and tenant binding when the execution repository accepts the
    // job repository's unit of work. A rejection leaves the execution repository usable for its
    // own stage.
    private static async Task<IUnitOfWork?> TryBeginSharedAsync(
        IRepository<TJob>       jobs,
        IRepository<TExecution> executions
    ) {
        IUnitOfWork? unit = null;
        try {
            unit = jobs.Begin();
            executions.Join(unit);
            return unit;
        } catch (InvalidOperationException) {
            if (unit is not null) {
                await unit.DisposeAsync();
            }

            return null;
        }
    }

    private static async Task<SettlementState> SettleAtomicAsync(
        JobExecutionLease<TJob, TExecution>  lease,
        IServiceProvider                     services,
        IRepository<TJob>                    jobs,
        IRepository<TExecution>              executions,
        IUnitOfWork                          unit,
        TExecution                           execution,
        JobExecutionResult<TJob>             result,
        SchedulingHandlerSupport<TJob, TExecution> support,
        CancellationToken                    ct
    ) {
        var now          = support.Scheduler.Time.GetUtcNow().UtcDateTime;
        var mutation     = services.GetRequiredService<IResourceMutation<TJob>>();
        TJob? persisted = null;
        Exception?   failure   = null;
        TJob? rearmed   = null;
        var          replayed  = 0;
        var          owned     = false;
        var          staged    = false;

        try {
            persisted = await FindJobAsync(jobs, execution, result.Job, ct);
            owned     = persisted is not null && persisted.ScheduleVersion == execution.ScheduleVersion;
            replayed  = owned ? Replayed(support, persisted!) : 0;
            Project(result.Job, result, now, support, replayed);

            if (owned) {
                CopyResult(result.Job, persisted!);

                if (await mutation.UpdateAsync(persisted!, unit, ct: ct) != MutationResult.Applied) {
                    failure = new FailedPreconditionException("Job settlement was not applied.");
                    await unit.RollbackAsync(ct);

                    return await RecoverExecutionAsync(lease, result, failure, ct);
                }

                if (persisted!.State == JobState.Active && persisted.NextRunTime is not null) {
                    try {
                        await support.ReplacePendingExecutionAsync(persisted, executions, services, unit, ct);
                    } catch (Exception error) {
                        failure = error;
                        await unit.RollbackAsync(ct);

                        return await RecoverExecutionAsync(lease, result, failure, ct);
                    }
                }
            }

            if (!await StageExecutionAsync(services, execution, result, unit, ct)) {
                await unit.RollbackAsync(ct);

                return new(new SettlementOutcome(false, false,
                    new FailedPreconditionException("Execution settlement was not applied.")), null, 0);
            }

            staged = true;

            await unit.CommitAsync(ct);

            if (owned && persisted!.State == JobState.Active && persisted.NextRunTime is not null) {
                rearmed = persisted;
            }

            return new(new SettlementOutcome(true, true, null), rearmed, replayed);
        } catch (Exception error) {
            failure ??= error;
            try {
                await unit.RollbackAsync(ct);
            } catch (Exception rollback) {
                failure = new AggregateException(failure, rollback);
            }

            // An execution write can rotate its concurrency stamp before a failed commit, so only
            // an attempt that never reached that write retries through a fresh scope.
            return staged
                ? new(new SettlementOutcome(false, false, failure), null, 0)
                : await RecoverExecutionAsync(lease, result, failure, ct);
        }
    }

    // A store pair that cannot share a transaction persists the schedule stage first: the job
    // row, then the next pending occurrence, and only then the claimed execution row.
    private static async Task<SettlementState> SettleSequentialAsync(
        IServiceProvider                     services,
        IJobExecutionStore<TJob, TExecution> store,
        TExecution                           execution,
        JobExecutionResult<TJob>             result,
        CancellationToken                    ct
    ) {
        var support   = services.GetRequiredService<SchedulingHandlerSupport<TJob, TExecution>>();
        var jobs      = services.GetService<IRepository<TJob>>();
        var executions = services.GetService<IRepository<TExecution>>();
        var now       = support.Scheduler.Time.GetUtcNow().UtcDateTime;
        var replayed  = 0;
        var settled   = false;
        var scheduled = false;
        Exception? failure = null;
        TJob? rearmed = null;

        if (jobs is null) {
            Project(result.Job, result, now, support, 0);
        } else {
            try {
                var mutation  = services.GetRequiredService<IResourceMutation<TJob>>();
                var persisted = await FindJobAsync(jobs, execution, result.Job, ct);
                var owned     = persisted is not null && persisted.ScheduleVersion == execution.ScheduleVersion;
                replayed      = owned ? Replayed(support, persisted!) : 0;
                Project(result.Job, result, now, support, replayed);

                if (owned) {
                    CopyResult(result.Job, persisted!);

                    await using (var unit = jobs.Begin()) {
                        if (await mutation.UpdateAsync(persisted!, unit, ct: ct) != MutationResult.Applied) {
                            failure = new FailedPreconditionException("Job settlement was not applied.");
                            await unit.RollbackAsync(ct);
                        } else {
                            await unit.CommitAsync(ct);

                            if (persisted!.State == JobState.Active && persisted.NextRunTime is not null) {
                                if (executions is not null) {
                                    await StagePendingAsync(services, executions, persisted, support, ct);
                                }

                                rearmed   = persisted;
                                scheduled = true;
                            } else {
                                scheduled = true;
                            }
                        }
                    }
                } else {
                    scheduled = true;
                }
            } catch (Exception error) {
                failure = error;
            }
        }

        try {
            settled = await SettleExecutionAsync(store, execution, result, ct);
        } catch (Exception error) {
            failure = failure is null ? error : new AggregateException(failure, error);
        }

        return new(new SettlementOutcome(settled, scheduled, failure), rearmed, replayed);
    }

    private static async Task StagePendingAsync(
        IServiceProvider                     services,
        IRepository<TExecution>              executions,
        TJob                                 job,
        SchedulingHandlerSupport<TJob, TExecution> support,
        CancellationToken                    ct
    ) {
        await using var unit = executions.Begin();
        await support.ReplacePendingExecutionAsync(job, executions, services, unit, ct);
        await unit.CommitAsync(ct);
    }

    private static async Task<SettlementState> RecoverExecutionAsync(
        JobExecutionLease<TJob, TExecution> lease,
        JobExecutionResult<TJob>            result,
        Exception                           failure,
        CancellationToken                   ct
    ) {
        try {
            var settled = await lease.RecoverAsync(
                (_, store, token) => SettleExecutionAsync(store, lease.Execution, result, token), ct);

            return new(new SettlementOutcome(settled, false, failure), null, 0);
        } catch (Exception error) {
            return new(new SettlementOutcome(false, false, new AggregateException(failure, error)), null, 0);
        }
    }

    private static async Task<bool> StageExecutionAsync(
        IServiceProvider    services,
        TExecution          execution,
        JobExecutionResult<TJob> result,
        IUnitOfWork         unit,
        CancellationToken   ct
    ) {
        AssignResult(execution, result);

        return await services.GetRequiredService<IResourceMutation<TExecution>>()
                             .UpdateAsync(execution, unit, ct: ct) == MutationResult.Applied;
    }

    private static Task<bool> SettleExecutionAsync(
        IJobExecutionStore<TJob, TExecution> store,
        TExecution                           execution,
        JobExecutionResult<TJob>             result,
        CancellationToken                    ct
    ) {
        AssignResult(execution, result);

        return store.TrySettleAsync(execution, ct);
    }

    private static void AssignResult(TExecution execution, JobExecutionResult<TJob> result) {
        execution.State           = result.State;
        execution.EndTime         = result.EndTime;
        execution.RecentError     = result.Error;
        execution.LeaseExpireTime = null;
    }

    private static void Project(TJob job, JobExecutionResult<TJob> result, DateTime now, SchedulingHandlerSupport<TJob, TExecution> support, int replayed) {
        job.RecentRunTime = result.EndTime;
        job.RecentError   = result.State == ExecutionState.Failed ? result.Error : null;

        var recurring = job.ScheduleType is ScheduleType.Cron or ScheduleType.Periodic;

        if (!recurring) {
            job.State       = result.State == ExecutionState.Failed ? JobState.Failed : JobState.Completed;
            job.NextRunTime = null;
            return;
        }

        if (result.State == ExecutionState.Failed) {
            job.State = JobState.Active;
        }

        if (job.State != JobState.Active) {
            return;
        }

        if (ComputeNextRunTime(job, now) is not { } due) {
            job.NextRunTime = null;
            return;
        }

        var options = support.Scheduler.Options.Value;
        job.NextRunTime = MissedFireWindow.Resolve(
            job, due, now, options.MissedFirePolicy, options.MaxMissedWalk, replayed);
    }

    private static DateTime? ComputeNextRunTime(TJob job, DateTime now) {
        if (job.ScheduleType == ScheduleType.Periodic && job is { NextRunTime: not null, IntervalTicks: not null }) {
            return job.NextRunTime.Value.AddTicks(job.IntervalTicks.Value);
        }

        return ScheduleDefinitionMapper.ToDefinition(job).GetNextRunTime(job.NextRunTime ?? now);
    }

    private static void CopyResult(TJob source, TJob target) {
        target.State         = source.State;
        target.RecentRunTime = source.RecentRunTime;
        target.RecentError   = source.RecentError;
        target.NextRunTime   = source.NextRunTime;
    }

    private static async Task<TJob?> FindJobAsync(
        IRepository<TJob>        jobs,
        TExecution               execution,
        TJob                     job,
        CancellationToken        ct
    ) {
        var identity = job.CanonicalName ?? job.Name ?? execution.Job;
        if (string.IsNullOrWhiteSpace(identity)) {
            return null;
        }

        return await jobs.FirstOrDefaultAsync(
            query => query.Where(row => row.CanonicalName == identity || row.Name == identity), ct);
    }

    private static int Replayed(SchedulingHandlerSupport<TJob, TExecution> support, TJob job) {
        var key = job.CanonicalName ?? job.Name;
        return key is not null && support.Scheduler.Entries.TryGetValue(key, out var entry)
            ? entry.ReplayedMisses
            : 0;
    }

    private readonly record struct SettlementState(SettlementOutcome Outcome, TJob? Rearmed, int Replayed);
}
