using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Entity-agnostic scheduler surface: lifecycle, job removal, and one-shot triggering.
///     Job-typed schedule operations are exposed by <see cref="IScheduler{TJob,TExecution}" />.
/// </summary>
public interface IScheduler
{
    /// <summary>Enables job dispatch.  Called by the host bootstrap.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Cancels every active timer and clears the in-memory registry.</summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>Removes the registry entry for <paramref name="job" />, if present.</summary>
    Task UnscheduleAsync(string job, CancellationToken ct);

    /// <summary>
    ///     One-shot invocation of <typeparamref name="TJobType" />. Materialised as
    ///     a <see cref="ScheduleType.OneTime" /> entry with
    ///     <see cref="SchemataJob.Replay" /> set to <c>false</c>;
    ///     <paramref name="context" />.<see cref="JobContext.Job" /> must be
    ///     unique per trigger. The implementation persists an execution row
    ///     (state <c>Pending</c>) before handing the row to the execution
    ///     dispatcher, so the returned row is immediately addressable under
    ///     <c>operations/{uid}</c>; the dispatcher transitions it to
    ///     <c>Succeeded</c> / <c>Failed</c> as the body runs.
    /// </summary>
    /// <remarks>
    ///     The Pending row is committed in its own unit of work resolved from the
    ///     framework service provider. Callers cannot join the commit to an outer
    ///     business transaction: if the outer transaction rolls back, the orphaned
    ///     Pending row is still drained by the dispatcher (and typically fails with
    ///     a missing handler when the business context that produced it never landed).
    ///     The contract provides eventual consistency.
    ///     <para>
    ///         The trigger enters the base command lifecycle directly. Scheduler-initiated fires
    ///         carry no caller identity; the user-facing <c>:run</c> method admits its caller at the
    ///         resource envelope before reaching the same lifecycle.
    ///     </para>
    /// </remarks>
    Task<SchemataJobExecution> TriggerAsync<TJobType>(JobContext context, CancellationToken ct)
        where TJobType : class, IScheduledJob;
}

/// <summary>
///     In-memory scheduler that fires <see cref="IScheduledJob" /> instances
///     according to their <see cref="IScheduleDefinition" />. Persistence is
///     delegated to <see cref="IJobLifecycleObserver" /> implementations.
/// </summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
public interface IScheduler<TJob, TExecution> : IScheduler
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    /// <summary>
    ///     Registers <paramref name="job" /> in the in-memory registry and arms
    ///     its timer.  Subsequent calls under the same name replace the entry.
    /// </summary>
    Task ScheduleAsync(TJob job, CancellationToken ct);

    /// <summary>
    ///     Registers <paramref name="job" /> with string variables mirrored to <see cref="SchemataJob.Variables" />.
    /// </summary>
    Task ScheduleAsync(TJob job, IReadOnlyDictionary<string, string?>? variables, CancellationToken ct);

    /// <summary>
    ///     Reschedules a persisted <paramref name="job" /> on host startup. When
    ///     <paramref name="preparedContext" /> carries an existing unfinished operation
    ///     execution, the fire reuses that row so a restarted operation completes its
    ///     original execution instead of creating a duplicate.
    /// </summary>
    Task RescheduleAsync(TJob job, JobContext? preparedContext, CancellationToken ct);
}
