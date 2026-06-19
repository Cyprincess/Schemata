using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     In-memory scheduler that fires <see cref="IScheduledJob" /> instances
///     according to their <see cref="IScheduleDefinition" />.  Persistence is
///     delegated to <see cref="IJobLifecycleObserver" /> implementations.
/// </summary>
public interface IScheduler
{
    /// <summary>Enables job dispatch.  Called by the host bootstrap.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Cancels every active timer and clears the in-memory registry.</summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>
    ///     Registers <paramref name="job" /> in the in-memory registry and arms
    ///     its timer.  Subsequent calls under the same name replace the entry.
    /// </summary>
    Task ScheduleAsync(SchemataJob job, CancellationToken ct);

    /// <summary>
    ///     Registers <paramref name="job" /> with typed variables owned by the Scheduling serializer.
    /// </summary>
    Task ScheduleAsync(SchemataJob job, IReadOnlyDictionary<string, object?>? variables, CancellationToken ct);

    /// <summary>Removes the registry entry for <paramref name="job" />, if present.</summary>
    Task UnscheduleAsync(string job, CancellationToken ct);

    /// <summary>
    ///     One-shot invocation of <typeparamref name="TJob" />. Materialised as
    ///     a <see cref="ScheduleType.OneTime" /> entry with
    ///     <see cref="SchemataJob.Replay" /> set to <c>false</c>;
    ///     <paramref name="context" />.<see cref="JobContext.Job" /> must be
    ///     unique per trigger. The implementation persists a
    ///     <see cref="SchemataJobExecution" /> row (state <c>Pending</c>) before
    ///     handing the row to the execution dispatcher, so the returned row is
    ///     immediately addressable under <c>operations/{uid}</c>; the dispatcher
    ///     transitions it to <c>Succeeded</c> / <c>Failed</c> as the body runs.
    /// </summary>
    /// <remarks>
    ///     The Pending row is committed in its own unit of work resolved from the
    ///     framework service provider. Callers cannot join the commit to an outer
    ///     business transaction: if the outer transaction rolls back, the orphaned
    ///     Pending row is still drained by the dispatcher (and typically fails with
    ///     a missing handler when the business context that produced it never landed).
    ///     The contract provides eventual consistency.
    /// </remarks>
    Task<SchemataJobExecution> TriggerAsync<TJob>(JobContext context, CancellationToken ct)
        where TJob : class, IScheduledJob;

    /// <summary>
    ///     Reschedules a persisted <paramref name="job" /> on host startup. When
    ///     <paramref name="preparedContext" /> carries an existing unfinished operation
    ///     execution, the fire reuses that row so a restarted operation completes its
    ///     original execution instead of creating a duplicate.
    /// </summary>
    Task RescheduleAsync(SchemataJob job, JobContext? preparedContext, CancellationToken ct);
}
