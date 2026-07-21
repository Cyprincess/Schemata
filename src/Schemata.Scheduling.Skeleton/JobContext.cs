using System;
using System.Collections.Generic;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>Per-fire context handed to <see cref="IScheduledJob.ExecuteAsync" />.</summary>
public class JobContext
{
    /// <summary>
    ///     AIP-122 canonical name of the originating <see cref="Entities.SchemataJob" /> for
    ///     this fire, or <see langword="null" /> when the fire was raised through
    ///     <see cref="IScheduler.TriggerAsync{TJob}" /> with no persistent scheduler entry.
    /// </summary>
    public string? Job { get; set; }

    /// <summary>Caller-supplied string variables, mirrored to/from <see cref="Entities.SchemataJob.Variables" />.</summary>
    public IReadOnlyDictionary<string, string?> Variables { get; set; } = new Dictionary<string, string?>();

    /// <summary>
    ///     Execution UID. When set by a caller (e.g. the <c>:run</c> handler) the
    ///     scheduler honours it so the response can carry the LRO name before the
    ///     timer fires; otherwise the scheduler allocates a fresh UID on fire.
    /// </summary>
    public Guid? ExecutionUid { get; set; }

    /// <summary>Scheduler-managed execution start time reserved for scheduler assignment.</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    ///     The custom method verb that dispatched this execution as a long-running
    ///     operation (e.g. <c>purge</c>), surfaced as <c>Operation.Metadata.Method</c>.
    ///     <c>null</c> for ordinary cron / periodic fires.
    /// </summary>
    public string? Method { get; set; }

    /// <summary>Stable job key that resolves this fire during scheduler recovery.</summary>
    public string? JobKey { get; set; }

    /// <summary>Serialized typed arguments persisted for restart-durable replay.</summary>
    public string? ArgsJson { get; set; }

    /// <summary>
    ///     Scheduler-built <see cref="SchemataJobExecution" /> row for this fire.
    ///     Set by <see cref="IScheduler.TriggerAsync{TJob}" /> and by the timer
    ///     fire path; consumed by <see cref="IJobLifecycleObserver" /> instances
    ///     (e.g. audit observer) as the source of truth for persistence. Reserved
    ///     for scheduler assignment.
    /// </summary>
    public SchemataJobExecution? Execution { get; set; }

}
