using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

public sealed partial class DefaultScheduler
{
    public async Task<SchemataJobExecution> TriggerAsync<TJob>(JobContext context, CancellationToken ct)
        where TJob : class, IScheduledJob {
        if (_stopped) {
            throw new InvalidOperationException("Scheduler is stopped; TriggerAsync is not accepting new fires.");
        }

        var registry = _services.GetRequiredService<IScheduledJobRegistry>();
        var jobKey   = registry.ResolveKey(typeof(TJob)) ?? typeof(TJob).FullName!;

        // The transient SchemataJob materialises the fire context; it is never persisted via
        // IRepository<SchemataJob>, so CanonicalName carries whatever the caller supplied
        // (a real `jobs/{leaf}` for RunJobHandler triggers, null for one-shot ops with no
        // persistent scheduler entry such as back-channel logout / push / purge).
        var job = new SchemataJob {
            Name          = context.Job,
            CanonicalName = context.Job,
            JobKey        = jobKey,
            ArgsJson      = context.ArgsJson,
            ScheduleType  = ScheduleType.OneTime,
            NextRunTime   = _time.GetUtcNow().UtcDateTime,
            Replay        = false,
            State         = JobState.Active,
            Variables     = new(context.Variables),
        };

        // A trigger is a one-shot occurrence: persist its Pending row synchronously so the returned
        // execution is immediately addressable as operations/{uid}. StartTime defaults to now (run
        // ASAP); a future StartTime defers the fire, so the same path schedules a future operation.
        context.ExecutionUid ??= Identifiers.NewUid();
        context.StartTime    ??= _time.GetUtcNow().UtcDateTime;
        context.JobKey       ??= jobKey;
        job.NextRunTime        = context.StartTime;
        context.Execution      = BuildExecution(job, context);

        await PersistExecutionAsync(context.Execution, ct);

        if (context.StartTime.Value <= _time.GetUtcNow().UtcDateTime) {
            // Due now: wake the dispatcher to drain the row immediately.
            SignalDispatcher();
        } else {
            // Future: arm a timer that signals at the due time. The Pending row is the durable
            // backstop, so the dispatcher's poll still fires it after a restart that loses the timer.
            await ArmOneShotTimerAsync(job);
        }

        return context.Execution;
    }

    private async Task ArmOneShotTimerAsync(SchemataJob job) {
        // The in-memory registry keys entries by canonical name when one exists, falling back
        // to the bare leaf for jobs that have not yet been routed through the canonical-name
        // advisor. Unnamed one-shot triggers (BCL / push / purge) skip
        // the registry entirely and rely on the dispatcher poll to drain the Pending row.
        var key = job.CanonicalName ?? job.Name;
        if (string.IsNullOrWhiteSpace(key)) {
            return;
        }

        ScheduledEntry entry;
        await _lock.WaitAsync();
        try {
            if (_stopped) {
                return;
            }

            if (_entries.TryRemove(key, out var existing)) {
                await existing.Cts.CancelAsync();
                existing.Cts.Dispose();
            }

            entry         = new(job, new());
            _entries[key] = entry;
        } finally {
            _lock.Release();
        }

        StartTimer(entry);
    }

    public Task RescheduleAsync(SchemataJob job, JobContext? preparedContext, CancellationToken ct) {
        // A reloaded job re-arms its timer and re-materializes (or adopts) its next Pending row;
        // the durable execution row, not an in-memory context, is the source of truth on restart.
        return ScheduleAsync(job, ct);
    }

    internal static SchemataJobExecution BuildExecution(SchemataJob job, JobContext context) {
        var uid        = context.ExecutionUid!.Value;
        var name       = uid.ToString("n");
        var descriptor = ResourceNameDescriptor.ForType<SchemataJobExecution>();

        return new() {
            Uid           = uid,
            Name          = name,
            CanonicalName = $"{descriptor.Collection}/{name}",
            Job           = job.CanonicalName,
            Method        = context.Method,
            JobKey        = context.JobKey ?? job.JobKey,
            ArgsJson      = context.ArgsJson ?? job.ArgsJson,
            Variables     = new(context.Variables),
            State         = ExecutionState.Pending,
            StartTime     = context.StartTime!.Value,
        };
    }

    private async Task PersistExecutionAsync(SchemataJobExecution execution, CancellationToken ct) {
        using var scope      = _services.CreateScope();
        var       executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();

        await executions.AddAsync(execution, ct);
        await executions.CommitAsync(ct);
    }
}
