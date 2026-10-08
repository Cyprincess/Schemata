using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Tenancy;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Advisors;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     The single executor for every <see cref="SchemataJobExecution" /> occurrence. The scheduler
///     materializes a <see cref="ExecutionState.Pending" /> row up front (immediate or future-dated)
///     and arms a timer that signals this dispatcher at the row's due time; the dispatcher drains
///     the pending set through <see cref="IJobExecutionStore" />, claims each row with a
///     <see cref="ExecutionState.Pending" /> → <see cref="ExecutionState.Running" /> transition
///     guarded by the concurrency token, runs the advisor / observer / job-body pipeline under a
///     renewed lease, and records the terminal state so the scheduling writer stages the job row
///     and re-arms recurring schedules. Multiple dispatchers can scale execution horizontally;
///     only the row claim serializes them.
/// </summary>
/// <remarks>
///     Lease, retry-ceiling, and crash-recovery semantics are owned here and carried by the
///     <see cref="IJobExecutionStore" /> contract: a claim holds a lease
///     (<see cref="JobRegistration.Lease" /> falling back to
///     <see cref="SchemataSchedulingOptions.ExecutionLease" />) that the dispatcher renews while
///     the body runs; when the lease lapses without renewal (host shutdown or crash), the row
///     returns to the pending set and any live dispatcher reclaims it. A job body that fails with
///     attempts remaining is requeued with linear backoff; the attempt that reaches
///     <see cref="JobRegistration.MaxAttempts" /> is settled <see cref="ExecutionState.Failed" />.
/// </remarks>
public sealed class JobExecutionDispatcher(
    IServiceProvider                 services,
    ILogger<JobExecutionDispatcher>? logger       = null,
    TimeProvider?                    time = null
) : BackgroundService
{
    private const           int           BatchSize = 100;
    private static readonly TimeSpan      Interval  = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan      DefaultLease   = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan      DefaultBackoff = TimeSpan.FromSeconds(30);
    private readonly        SemaphoreSlim _pending  = new(0, int.MaxValue);
    private readonly        ConcurrentDictionary<string, CancellationTokenSource> _running =
        services.GetService<ConcurrentDictionary<string, CancellationTokenSource>>() ?? new();
    private readonly        TimeProvider  _time     = time ?? TimeProvider.System;


    /// <summary>Wakes the dispatch loop after a producer commits (or a timer arms) a due execution row.</summary>
    public void NotifyPending() {
        _pending.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        while (!st.IsCancellationRequested) {
            try {
                await DispatchPendingAsync(st);
                await _pending.WaitAsync(Interval, st);
            } catch (OperationCanceledException) when (st.IsCancellationRequested) {
                return;
            } catch (Exception ex) {
                logger?.LogError(ex, "Job execution dispatch pass failed; retrying next interval.");
            }
        }
    }

    /// <summary>Claims and runs every row in the pending set in a scoped dispatch pass.</summary>
    public async Task DispatchPendingAsync(CancellationToken ct) {
        using var host  = TenantContext.Enter(TenantIdentity.Host);
        using var scope = services.CreateScope();
        var       store = scope.ServiceProvider.GetRequiredService<IJobExecutionStore>();
        var       now   = _time.GetUtcNow().UtcDateTime;
        var       due   = new List<SchemataJobExecution>();

        // A Pending row whose StartTime is still in the future is a scheduled occurrence or a
        // backed-off retry; only rows that have come due are the dispatcher's concern. A Running
        // row with a live lease is owned by its claimant; a Running row whose lease lapsed
        // returns to the pending set for crash recovery. Running rows without a lease belong to
        // an in-process LRO client and are never listed.
        await foreach (var row in store.ListDueAsync(now, BatchSize, ct)) {
            due.Add(row);
        }

        foreach (var row in due) {
            await DispatchAsync(scope.ServiceProvider, store, row, ct);
        }
    }

    private async Task DispatchAsync(
        IServiceProvider     serviceProvider,
        IJobExecutionStore   store,
        SchemataJobExecution execution,
        CancellationToken    ct
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

        MessageExecutionScope prepared;
        var message = new MessageContext(new Dictionary<string, string?> { [MessageContexts.TenantIdKey] = execution.Tenant });
        try {
            prepared = await services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(message, ct);
        } catch (Exception error) {
            await MarkFailedAsync(store, execution, error.Message, CancellationToken.None);
            throw;
        }
        using var identity = prepared.Enter();
        await using var owned = prepared;
        try {
            await prepared.RestoreAsync(message, ct);
        } catch (Exception error) {
            await MarkFailedAsync(store, execution, error.Message, CancellationToken.None);
            throw;
        }
        await RunPipelineAsync(prepared.Services, execution, policy, ct);
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
        IServiceProvider     serviceProvider,
        SchemataJobExecution execution,
        ExecutionPolicy      policy,
        CancellationToken    ct
    ) {
        var store    = serviceProvider.GetRequiredService<IJobExecutionStore>();
        var registry = serviceProvider.GetRequiredService<IScheduledJobRegistry>();
        var jobType  = registry.Resolve(execution.JobKey!);
        if (jobType is null) {
            await MarkFailedAsync(store, execution, $"Job key '{execution.JobKey}' is not registered.", ct);
            return;
        }

        var observers = serviceProvider.GetServices<IJobLifecycleObserver>().ToList();

        SchemataJob? job     = null;
        JobContext?  context = null;
        AdviseResult advise;
        var       adviceCtx   = new AdviceContext(serviceProvider);
        using var adviceScope = AdviceContext.Establish(adviceCtx);
        try {
            job     = await LoadOrSynthesizeJobAsync(serviceProvider, execution, ct);
            context = BuildContext(execution);
            adviceCtx.Set(job);

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
            await FinalizeAsync(serviceProvider, store, job, execution, ExecutionState.Failed, ex, observers,
                                context, ct);
            return;
        }

        switch (advise) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
            default:
                // An advisor handled or blocked the fire before it ran; leave no terminal row churn
                // beyond the claim and advance the schedule so the next occurrence is materialized.
                await FinalizeAsync(serviceProvider, store, job!, execution, ExecutionState.Skipped, null, observers,
                                    context!, ct, notifySkipped: true);
                return;
            case AdviseResult.Block:
                await FinalizeAsync(serviceProvider, store, job!, execution, ExecutionState.Blocked, null, observers,
                                    context!, ct, notifyBlocked: true);
                return;
        }


        try {
            var scheduledJob = (IScheduledJob)serviceProvider.GetRequiredService(jobType);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var key = execution.Uid.ToString("n");
            if (!_running.TryAdd(key, linked)) {
                return;
            }

            var renewal = RenewLeaseAsync(store, execution, policy.Lease, linked, linked.Token);
            try {
                await scheduledJob.ExecuteAsync(context!, linked.Token);
            } finally {
                _running.TryRemove(key, out _);
                await linked.CancelAsync();
                await renewal;
            }

            execution.Output = context!.Execution?.Output;
            await FinalizeAsync(serviceProvider, store, job!, execution, ExecutionState.Succeeded, null, observers,
                                context!, ct, true);
        } catch (OperationCanceledException) {
            // Host shutdown or lease loss mid-run: leave the row Running so a later pass reclaims
            // and reruns it once the lease lapses.
            logger?.LogInformation("Execution '{ExecutionUid}' cancelled mid-run; leaving it for re-dispatch.",
                                   execution.Uid);
        } catch (Exception ex) {
            if (execution.Attempt < policy.MaxAttempts) {
                execution.RecentError = ex.Message;
                var next = _time.GetUtcNow().UtcDateTime.Add(policy.Backoff * execution.Attempt);
                if (await store.TryRequeueAsync(execution, next, ct)) {
                    logger?.LogInformation("Execution '{ExecutionUid}' failed attempt {Attempt}; requeued for {NextStartUtc}.",
                                           execution.Uid, execution.Attempt, next);
                }

                return;
            }

            await FinalizeAsync(serviceProvider, store, job!, execution, ExecutionState.Failed, ex, observers,
                                context!, ct, notifyFailed: true);
        }
    }

    /// <summary>
    ///     Extends the claimed row's lease at half-lease intervals while the job body runs. A
    ///     rejected renewal means the row moved on under another worker (e.g. <c>:cancel</c>), so
    ///     the body is cancelled and the other transition honoured. A faulting renewal is logged
    ///     and retried at the next interval; it never faults this loop, so the body's outcome is
    ///     the only result the caller observes. The cadence is a wall-clock heartbeat, so it waits
    ///     on the system clock rather than the schedulable <see cref="TimeProvider" />.
    /// </summary>
    private async Task RenewLeaseAsync(
        IJobExecutionStore      store,
        SchemataJobExecution    execution,
        TimeSpan                lease,
        CancellationTokenSource linked,
        CancellationToken       ct
    ) {
        var interval = TimeSpan.FromTicks(lease.Ticks / 2);
        while (true) {
            try {
                await Task.Delay(interval, ct);
            } catch (OperationCanceledException) {
                return;
            }

            bool renewed;
            try {
                var leaseExpire = _time.GetUtcNow().UtcDateTime.Add(lease);
                renewed = await store.TryRenewLeaseAsync(execution, leaseExpire, CancellationToken.None);
            } catch (Exception error) {
                logger?.LogWarning(error, "Lease renewal for execution '{ExecutionUid}' faulted; retrying next interval.",
                                   execution.Uid);
                continue;
            }

            if (renewed) {
                continue;
            }

            try {
                await linked.CancelAsync();
            } catch (ObjectDisposedException) {
                // The body completed while its lease loss was being observed.
            }

            return;
        }
    }

    /// <summary>
    ///     Writes the terminal execution row, stages the job-row result through the scheduling
    ///     writer, then runs the matching lifecycle observers against the post-write job fields.
    ///     The claimed <paramref name="execution" /> instance carries its expected concurrency
    ///     token, so a concurrent cancellation aborts finalization instead of overwriting it.
    /// </summary>
    private async Task FinalizeAsync(
        IServiceProvider                  serviceProvider,
        IJobExecutionStore                store,
        SchemataJob                       job,
        SchemataJobExecution              execution,
        ExecutionState                    state,
        Exception?                        exception,
        List<IJobLifecycleObserver>       observers,
        JobContext                        context,
        CancellationToken                 ct,
        bool                              notifySucceeded = false,
        bool                              notifyFailed    = false,
        bool                              notifyBlocked   = false,
        bool                              notifySkipped   = false
    ) {
        execution.State       = state;
        execution.EndTime     = _time.GetUtcNow().UtcDateTime;
        execution.RecentError = exception?.Message;

        // Row moved on under another worker (e.g. :cancel) after we claimed it; honour that.
        if (!await store.TrySettleAsync(execution, ct)) {
            return;
        }

        // Advance the in-memory job row so observers always see the post-fire view; the staging handler
        // persists these fields only when the persisted job row exists.
        job.RecentRunTime = execution.EndTime;
        job.RecentError   = state == ExecutionState.Failed ? exception?.Message : null;

        var recurring = job.ScheduleType is ScheduleType.Cron or ScheduleType.Periodic;

        if (state == ExecutionState.Failed) {
            job.State = recurring ? JobState.Active : JobState.Failed;
        } else if (!recurring) {
            job.State       = JobState.Completed;
            job.NextRunTime = null;
        }

        // Recurring active jobs carry their next occurrence in the staged payload; the staging
        // handler recomputes from the freshly loaded persisted schedule only when the FireAll
        // missed-fire walk exhausts its cap.
        if (recurring && job is { State: JobState.Active }) {
            job.NextRunTime = ComputeNextRunTime(job);
        }

        var identity = job.CanonicalName ?? job.Name ?? execution.Job;
        if (!string.IsNullOrWhiteSpace(identity)) {
            var dispatcher = serviceProvider.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<StageJobExecutionResultRequest, Unit>(
                new(identity, job.State, job.RecentRunTime, job.RecentError, job.NextRunTime, execution.ScheduleVersion), ct);
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

    private async Task<SchemataJob> LoadOrSynthesizeJobAsync(
        IServiceProvider     serviceProvider,
        SchemataJobExecution execution,
        CancellationToken    ct
    ) {
        var jobs      = serviceProvider.GetRequiredService<IRepository<SchemataJob>>();
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

    // One-shot triggers and durable operations carry no persisted SchemataJob row; the transient
    // shell gives the advisor / observer pipeline a job to reason about. CanonicalName mirrors
    // execution.Job so downstream lookups stay consistent.
    private static SchemataJob SynthesizeJob(SchemataJobExecution execution) {
        return new() {
            Tenant        = execution.Tenant,
            CanonicalName = execution.Job,
            JobKey        = execution.JobKey,
            ArgsJson      = execution.ArgsJson,
            ScheduleType  = ScheduleType.OneTime,
            Replay        = false,
            State         = JobState.Active,
        };
    }

    private static JobContext BuildContext(SchemataJobExecution execution) {
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
        if (!_running.TryGetValue(executionUid.ToString("n"), out var source)) {
            return false;
        }

        try {
            source.Cancel();
            return true;
        } catch (ObjectDisposedException) {
            return false;
        }
    }

    private DateTime? ComputeNextRunTime(SchemataJob job) {
        if (job.ScheduleType == ScheduleType.Periodic && job is { NextRunTime: not null, IntervalTicks: not null }) {
            return job.NextRunTime.Value.AddTicks(job.IntervalTicks.Value);
        }

        var schedule = ScheduleDefinitionMapper.ToDefinition(job);
        return schedule.GetNextRunTime(job.NextRunTime ?? _time.GetUtcNow().UtcDateTime);
    }

    private async Task MarkFailedAsync(
        IJobExecutionStore   store,
        SchemataJobExecution execution,
        string               error,
        CancellationToken    ct
    ) {
        execution.State       = ExecutionState.Failed;
        execution.EndTime     = _time.GetUtcNow().UtcDateTime;
        execution.RecentError = error;

        // Another dispatcher transitioning the row first is honoured by the settle.
        await store.TrySettleAsync(execution, ct);
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
