using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Settlement;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Advisors;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Services;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     The single executor for every <typeparamref name="TExecution" /> occurrence. The scheduler
///     materializes a <see cref="ExecutionState.Pending" /> row up front (immediate or future-dated)
///     and arms a timer that signals this dispatcher at the row's due time; the dispatcher drains
///     the pending set through <see cref="IJobExecutionStore{TJob,TExecution}" />, claims each row
///     with a <see cref="ExecutionState.Pending" /> → <see cref="ExecutionState.Running" />
///     transition guarded by the concurrency token, runs the advisor / observer / job-body pipeline
///     under a renewed lease, and records the terminal state so the scheduling writer stages the
///     job row and re-arms recurring schedules. Multiple dispatchers can scale execution
///     horizontally; only the row claim serializes them.
/// </summary>
/// <remarks>
///     Lease, retry-ceiling, and crash-recovery semantics are owned here and carried by the
///     <see cref="IJobExecutionStore{TJob,TExecution}" /> contract: a claim holds a lease
///     (<see cref="JobRegistration.Lease" /> falling back to
///     <see cref="SchemataSchedulingOptions.ExecutionLease" />) renewed from claim through
///     setup and termination; when the lease lapses without renewal (host shutdown or crash), the row
///     returns to the pending set and any live dispatcher reclaims it. A job body that fails with
///     attempts remaining is requeued with linear backoff; the attempt that reaches
///     <see cref="JobRegistration.MaxAttempts" /> is settled <see cref="ExecutionState.Failed" />.
/// </remarks>
public sealed class JobExecutionDispatcher<TJob, TExecution>(
    IServiceProvider                                    services,
    ILogger<JobExecutionDispatcher<TJob, TExecution>>?  logger       = null,
    TimeProvider?                                       time = null
) : BackgroundService
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    private const           int           BatchSize = 100;
    private static readonly TimeSpan      Interval  = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan      DefaultLease   = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan      DefaultBackoff = TimeSpan.FromSeconds(30);
    private readonly        SemaphoreSlim _pending  = new(0, int.MaxValue);
    private readonly        IActiveExecutionRegistry _executions = services.GetRequiredService<IActiveExecutionRegistry>();
    private readonly        TimeProvider  _time     = time ?? TimeProvider.System;


    /// <summary>Wakes the dispatch loop after a producer commits (or a timer arms) a due execution row.</summary>
    public void NotifyPending() {
        _pending.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        while (!st.IsCancellationRequested) {
            try {
                await DispatchPendingAsync(st);
            } catch (OperationCanceledException) when (st.IsCancellationRequested) {
                return;
            } catch (Exception ex) {
                logger?.LogError(ex, "Job execution dispatch pass failed; retrying next interval.");
            }
            using var interval = new CancellationTokenSource(Interval, _time);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(st, interval.Token);
            try {
                await _pending.WaitAsync(wait.Token);
            } catch (OperationCanceledException) when (interval.IsCancellationRequested && !st.IsCancellationRequested) {
                continue;
            } catch (OperationCanceledException) when (st.IsCancellationRequested) {
                return;
            }
        }
    }

    /// <summary>Claims and runs every row in the pending set in a scoped dispatch pass.</summary>
    public async Task DispatchPendingAsync(CancellationToken ct) {
        await using var scope = services.CreateAsyncScope();
        var       store = scope.ServiceProvider.GetRequiredService<IJobExecutionStore<TJob, TExecution>>();
        var       now   = _time.GetUtcNow().UtcDateTime;
        var       due   = new List<TExecution>();
        await store.FailOrphanedRunningAsync(now, ct);

        // A Pending row whose StartTime is still in the future is a scheduled occurrence or a
        // backed-off retry; only rows that have come due are the dispatcher's concern. A Running
        // row with a live lease is owned by its claimant; a Running row whose lease lapsed
        // returns to the pending set for crash recovery. Inline operations have no JobKey;
        // their expired ownership is settled by the store sweep.
        await foreach (var row in store.ListDueAsync(now, BatchSize, ct)) {
            due.Add(row);
        }

        foreach (var row in due) {
            await using var rowScope = services.CreateAsyncScope();
            var scoped = rowScope.ServiceProvider.GetRequiredService<IJobExecutionStore<TJob, TExecution>>();
            await DispatchAsync(rowScope.ServiceProvider, scoped, row, ct);
        }
    }

    private async Task DispatchAsync(
        IServiceProvider                     serviceProvider,
        IJobExecutionStore<TJob, TExecution> store,
        TExecution                           execution,
        CancellationToken                    ct
    ) {
        if (string.IsNullOrWhiteSpace(execution.JobKey)) {
            await MarkFailedAsync(store, execution, "Job execution is missing its JobKey.", ct);
            return;
        }

        var registry = serviceProvider.GetService<IScheduledJobRegistry>();
        var jobType  = registry?.Resolve(execution.JobKey);

        // A misconfigured policy condemns only this execution; the pass continues for other rows.
        ExecutionPolicy policy;
        try {
            policy = ResolvePolicy(jobType);
        } catch (Exception error) {
            await MarkFailedAsync(store, execution, error.Message, ct);
            return;
        }

        // Claim the row before execution to serialize competing dispatchers; the lease bounds how
        // long a crashed claimant can hold the row. A lost race means another dispatcher won.
        var leaseExpire = _time.GetUtcNow().UtcDateTime.Add(policy.Lease);
        if (!await store.TryClaimAsync(execution, leaseExpire, ct)) {
            return;
        }

        await using var lease = new JobExecutionLease<TJob, TExecution>(JobExecutionScopes.Plain(services.GetRequiredService<IServiceScopeFactory>()), execution,
            policy.Lease, _time, _executions, ct);
        try {
            lease.ThrowIfOwnershipLost();
            await lease.ConfirmAsync(lease.Token);
            lease.ThrowIfOwnershipLost();
            await RunPipelineAsync(serviceProvider, execution, policy, lease);
        } catch (OperationCanceledException) when (lease.Token.IsCancellationRequested) {
            return;
        } catch (Exception error) {
            await lease.TransitionAsync(current => MarkFailedAsync(current, execution, error.Message, lease.Token), lease.Token);
            throw;
        }
    }

    /// <summary>
    ///     Runs the advisor → observer → job-body pipeline for a claimed execution row, records the
    ///     terminal state, and stages the job-row result. Pipeline context and scheduling state
    ///     are sourced from the durable row. A failure before the job body (job resolution,
    ///     advisors, lifecycle publication) settles the claimed row as
    ///     <see cref="ExecutionState.Failed" /> through the same finalize owner. A job-body
    ///     failure with attempts remaining is requeued with backoff and no observer fires until
    ///     the attempt that exhausts <see cref="ExecutionPolicy.MaxAttempts" />, which settles
    ///     <see cref="ExecutionState.Failed" /> and notifies
    ///     <see cref="IJobLifecycleObserver.OnFailedAsync" />.
    /// </summary>
    private async Task RunPipelineAsync(
        IServiceProvider                     serviceProvider,
        TExecution                           execution,
        ExecutionPolicy                      policy,
        JobExecutionLease<TJob, TExecution>  lease
    ) {
        var ct = lease.Token;
        var registry = serviceProvider.GetRequiredService<IScheduledJobRegistry>();
        var jobType  = registry.Resolve(execution.JobKey!);
        if (jobType is null) {
            await lease.TransitionAsync(current => MarkFailedAsync(current, execution, $"Job key '{execution.JobKey}' is not registered.", ct), ct);
            return;
        }

        var observers = serviceProvider.GetServices<IJobLifecycleObserver>().ToList();

        TJob?       job     = null;
        JobContext? context = null;
        AdviseResult advise;
        var adviceCtx = new AdviceContext(serviceProvider);
        try {
            job     = await LoadOrSynthesizeJobAsync(serviceProvider, execution, ct);
            context = BuildContext(execution);

            advise = await Advisor.For<IJobExecutionAdvisor>().RunAsync(adviceCtx, context, ct);
            if (advise == AdviseResult.Continue) {
                foreach (var observer in observers) {
                    await observer.OnTriggeredAsync(job, context, ct);
                }
            }
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            // Host shutdown mid-setup leaves the claimed row Running for a later pass to reclaim.
            throw;
        } catch (Exception ex) {
            // Job resolution, advisor, or publication failed after the claim and the body never
            // ran, so no job-outcome observer fires; settle the row Failed and let the staging
            // writer advance a recurring schedule.
            job     ??= SynthesizeJob(execution);
            context ??= BuildContext(execution);
            await FinalizeAsync(serviceProvider, job, execution, ExecutionState.Failed, ex, observers, context, lease, ct);
            return;
        }

        switch (advise) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
            default:
                // An advisor handled or blocked the fire before it ran; leave no terminal row churn
                // beyond the claim and advance the schedule so the next occurrence is materialized.
                await FinalizeAsync(serviceProvider, job!, execution, ExecutionState.Skipped, null, observers,
                    context!, lease, ct, notifySkipped: true);
                return;
            case AdviseResult.Block:
                await FinalizeAsync(serviceProvider, job!, execution, ExecutionState.Blocked, null, observers,
                    context!, lease, ct, notifyBlocked: true);
                return;
        }


        try {
            var scheduledJob = (IScheduledJob)serviceProvider.GetRequiredService(jobType);
            lease.ThrowIfOwnershipLost();
            await scheduledJob.ExecuteAsync(context!, ct);
            lease.ThrowIfOwnershipLost();
            execution.Output = context!.Execution?.Output;
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            return;
        } catch (Exception ex) {
            if (execution.Attempt < policy.MaxAttempts) {
                var next = _time.GetUtcNow().UtcDateTime.Add(policy.Backoff * execution.Attempt);
                if (await lease.TransitionAsync(current => {
                    execution.RecentError = ex.Message;
                    return current.TryRequeueAsync(execution, next, ct);
                }, ct)) {
                    logger?.LogInformation("Execution '{ExecutionUid}' failed attempt {Attempt}; requeued for {NextStartUtc}.",
                                           execution.Uid, execution.Attempt, next);
                }

                return;
            }

            await FinalizeAsync(serviceProvider, job!, execution, ExecutionState.Failed, ex, observers,
                context!, lease, ct, notifyFailed: true);
            return;
        }
        await FinalizeAsync(serviceProvider, job!, execution, ExecutionState.Succeeded, null, observers,
            context!, lease, ct, true);
    }


    /// <summary>
    ///     Hands the claimed execution and its terminal result to the settlement owner, then runs
    ///     the matching lifecycle observers against the post-write job fields. The claimed
    ///     <paramref name="execution" /> instance carries its expected concurrency token, so a
    ///     concurrent cancellation aborts finalization instead of overwriting it.
    /// </summary>
    private async Task FinalizeAsync(
        IServiceProvider                     serviceProvider,
        TJob                                 job,
        TExecution                           execution,
        ExecutionState                       state,
        Exception?                           exception,
        List<IJobLifecycleObserver>          observers,
        JobContext                           context,
        JobExecutionLease<TJob, TExecution>  lease,
        CancellationToken                    ct,
        bool                                 notifySucceeded = false,
        bool                                 notifyFailed    = false,
        bool                                 notifyBlocked   = false,
        bool                                 notifySkipped   = false
    ) {
        var settlement = serviceProvider.GetRequiredService<IJobSettlement<TJob, TExecution>>();
        var outcome = await settlement.SettleAsync(
            lease, new(job, state, _time.GetUtcNow().UtcDateTime, exception?.Message), ct);

        if (!outcome.ExecutionSettled) {
            // A concurrency conflict means another worker transitioned the claimed row first;
            // that transition owns the terminal state and this finalization stops without
            // overwriting it. Any other failure kept the row unpersisted and surfaces here.
            if (outcome.Failure is not null and not AbortedException) {
                ExceptionDispatchInfo.Capture(outcome.Failure).Throw();
            }

            return;
        }

        if (outcome.Failure is not null) {
            logger?.LogWarning(outcome.Failure,
                "Job settlement recorded execution '{ExecutionUid}' without a fully persisted schedule.",
                execution.Uid);
        }

        foreach (var observer in observers) {
            try {
                if (notifySucceeded) {
                    await observer.OnSucceededAsync(job, context, ct);
                } else if (notifyFailed && exception is { } failure) {
                    await observer.OnFailedAsync(job, context, failure, ct);
                } else if (notifyBlocked) {
                    await observer.OnBlockedAsync(job, context, ct);
                } else if (notifySkipped) {
                    await observer.OnSkippedAsync(job, context, ct);
                }
            } catch (Exception observerEx) {
                logger?.LogWarning(observerEx, "Lifecycle observer threw while finalizing job '{JobName}'.", job.Name);
            }
        }
    }

    private async Task<TJob> LoadOrSynthesizeJobAsync(
        IServiceProvider serviceProvider,
        TExecution       execution,
        CancellationToken ct
    ) {
        var jobs      = serviceProvider.GetRequiredService<IRepository<TJob>>();
        var canonical = execution.Job;
        if (!string.IsNullOrWhiteSpace(canonical)) {
            var existing = await jobs.FirstOrDefaultAsync(
                q => q.Where(j => j.CanonicalName == canonical), ct);
            if (existing is not null) {
                return existing;
            }
        }

        return SynthesizeJob(execution);
    }

    // One-shot triggers and durable operations carry no persisted job row; the transient
    // shell gives the advisor / observer pipeline a job to reason about. CanonicalName mirrors
    // execution.Job so downstream lookups stay consistent.
    private static TJob SynthesizeJob(TExecution execution) {
        return new() {
            MessageContext = execution.MessageContext is null ? null : new(execution.MessageContext),
            CanonicalName = execution.Job,
            JobKey        = execution.JobKey,
            ArgsJson      = execution.ArgsJson,
            ScheduleType  = ScheduleType.OneTime,
            Replay        = false,
            State         = JobState.Active,
        };
    }

    private static JobContext BuildContext(TExecution execution) {
        return new() {
            Job          = execution.Job,
            ExecutionUid = execution.Uid,
            StartTime    = execution.StartTime,
            Method       = execution.Method,
            JobKey       = execution.JobKey,
            ArgsJson     = execution.ArgsJson,
            Variables    = execution.Variables ?? new Dictionary<string, string?>(),
            Execution    = execution,
        };
    }

    internal bool TryCancel(Guid executionUid) {
        return _executions.Cancel(executionUid.ToString("n"));
    }

    private async Task<bool> MarkFailedAsync(
        IJobExecutionStore<TJob, TExecution> store,
        TExecution                           execution,
        string                               error,
        CancellationToken                    ct
    ) {
        execution.State       = ExecutionState.Failed;
        execution.EndTime     = _time.GetUtcNow().UtcDateTime;
        execution.RecentError = error;

        // Another dispatcher transitioning the row first is honoured by the settle.
        return await store.TrySettleAsync(execution, ct);
    }

    /// <summary>
    ///     Resolves the lease, retry ceiling, and backoff for <paramref name="jobType" /> from its
    ///     <see cref="JobRegistration" /> on <see cref="SchemataSchedulingOptions.Jobs" />, falling
    ///     back to one attempt and the option defaults when the job has no registration.
    /// </summary>
    private ExecutionPolicy ResolvePolicy(Type? jobType) {
        var options      = services.GetService<IOptions<SchemataSchedulingOptions>>()?.Value;
        var registration = jobType is null ? null : options?.Jobs.FirstOrDefault(j => j.JobType == jobType);

        var lease = registration?.Lease ?? options?.ExecutionLease ?? DefaultLease;
        if (lease <= TimeSpan.Zero) {
            throw new InvalidOperationException("Execution lease must be greater than zero.");
        }

        var maxAttempts = registration?.MaxAttempts ?? 1;
        var backoff     = registration?.RetryBackoff ?? DefaultBackoff;
        return new(maxAttempts < 1 ? 1 : maxAttempts, backoff, lease);
    }

    /// <summary>Lease and retry settings resolved for one job type at dispatch time.</summary>
    internal readonly record struct ExecutionPolicy(int MaxAttempts, TimeSpan Backoff, TimeSpan Lease);
}
