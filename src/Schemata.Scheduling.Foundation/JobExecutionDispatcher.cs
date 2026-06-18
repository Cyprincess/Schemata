using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Background service that drains <see cref="SchemataJobExecution" /> rows left in
///     <see cref="ExecutionState.Pending" /> by <see cref="IScheduler.TriggerAsync{TJob}" />
///     (or any other producer), claims each so a single worker runs it, and records the terminal
///     state. Once a row is claimed (<see cref="ExecutionState.Running" />) the dispatcher's work is
///     finished: a worker crash or a long-running operation that needs a timeout is an LRO /
///     application concern, not a re-claim performed here. Job resolution goes through
///     <see cref="IScheduledJobRegistry" />; unknown keys or construction failures transition the
///     row to <see cref="ExecutionState.Failed" />.
/// </summary>
public sealed class JobExecutionDispatcher(
    IServiceProvider                 services,
    ILogger<JobExecutionDispatcher>? logger       = null,
    TimeProvider?                    timeProvider = null
) : BackgroundService
{
    private const           int           BatchSize = 100;
    private static readonly TimeSpan      Interval  = TimeSpan.FromSeconds(30);
    private readonly        SemaphoreSlim _pending  = new(0, int.MaxValue);
    private readonly        TimeProvider  _time     = timeProvider ?? TimeProvider.System;

    /// <summary>Wakes the dispatch loop after a producer commits a Pending execution row.</summary>
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
                logger?.LogWarning(ex, "Job execution dispatch pass failed; retrying next interval.");
            }
        }
    }

    public async Task DispatchPendingAsync(CancellationToken ct) {
        using var scope      = services.CreateScope();
        var       executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var       pending    = new List<SchemataJobExecution>();

        // Only Pending rows are the dispatcher's concern; a Running row has already been claimed, and
        // its completion or timeout is owned by the worker / LRO client rather than re-claimed here.
        await foreach (var row in executions.ListAsync(
                           q => q.Where(e => e.State == ExecutionState.Pending).Take(BatchSize),
                           ct)) {
            pending.Add(row);
        }

        foreach (var row in pending) {
            await DispatchAsync(scope.ServiceProvider, executions, row, ct);
        }
    }

    private async Task DispatchAsync(
        IServiceProvider                  serviceProvider,
        IRepository<SchemataJobExecution> executions,
        SchemataJobExecution              execution,
        CancellationToken                 ct
    ) {
        if (string.IsNullOrWhiteSpace(execution.JobKey)) {
            await MarkFailedAsync(executions, execution, "Job execution is missing its JobKey.", ct);
            return;
        }

        // Claim the row so a second dispatcher does not run the same execution.
        execution.State = ExecutionState.Running;
        try {
            await executions.UpdateAsync(execution, ct);
            await executions.CommitAsync(ct);
        } catch (ConcurrencyException) {
            return;
        }

        try {
            var registry = serviceProvider.GetRequiredService<IScheduledJobRegistry>();
            var jobType  = registry.Resolve(execution.JobKey);
            if (jobType is null) {
                throw new InvalidOperationException($"Job key '{execution.JobKey}' is not registered.");
            }

            var job = (IScheduledJob)serviceProvider.GetRequiredService(jobType);
            var context = new JobContext {
                Job          = execution.Job!,
                ExecutionUid = execution.Uid,
                StartTime    = execution.StartTime,
                JobKey       = execution.JobKey,
                ArgsJson     = execution.ArgsJson,
                Execution    = execution,
            };

            await job.ExecuteAsync(context, ct);
            execution.State       = ExecutionState.Succeeded;
            execution.EndTime     = _time.GetUtcNow().UtcDateTime;
            execution.RecentError = null;
            execution.Output      = context.Execution?.Output;
        } catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) {
            execution.State       = ExecutionState.Failed;
            execution.EndTime     = _time.GetUtcNow().UtcDateTime;
            execution.RecentError = ex.Message;
        }

        try {
            await executions.UpdateAsync(execution, ct);
            await executions.CommitAsync(ct);
        } catch (ConcurrencyException) {
            // Row moved on under another worker after we claimed it; nothing to do.
        }
    }

    private async Task MarkFailedAsync(
        IRepository<SchemataJobExecution> executions,
        SchemataJobExecution              execution,
        string                            error,
        CancellationToken                 ct
    ) {
        execution.State       = ExecutionState.Failed;
        execution.EndTime     = _time.GetUtcNow().UtcDateTime;
        execution.RecentError = error;

        try {
            await executions.UpdateAsync(execution, ct);
            await executions.CommitAsync(ct);
        } catch (ConcurrencyException) {
            // Another dispatcher already transitioned the row; nothing to do.
        }
    }
}
