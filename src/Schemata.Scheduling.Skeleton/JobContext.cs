using System;
using System.Collections.Generic;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>Per-fire context handed to <see cref="IScheduledJob.ExecuteAsync" />.</summary>
public class JobContext
{
    /// <summary>Canonical name of the job entry being fired.</summary>
    public string Job { get; set; } = null!;

    /// <summary>Caller-supplied variables, serialized to/from <see cref="Entities.SchemataJob.Variables" />.</summary>
    public IReadOnlyDictionary<string, object?> Variables { get; set; } = new Dictionary<string, object?>();

    /// <summary>
    ///     Execution UID. When set by a caller (e.g. the <c>:run</c> handler) the
    ///     scheduler honours it so the response can carry the LRO name before the
    ///     timer fires; otherwise the scheduler allocates a fresh UID on fire.
    /// </summary>
    public Guid? ExecutionUid { get; set; }

    /// <summary>Scheduler-managed execution start time.  Jobs MUST NOT assign this.</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    ///     Scheduler-built <see cref="SchemataJobExecution" /> row for this fire.
    ///     Set by <see cref="IScheduler.TriggerAsync{TJob}" /> and by the timer
    ///     fire path; consumed by <see cref="IJobLifecycleObserver" /> instances
    ///     (e.g. audit observer) as the source of truth for persistence. Jobs
    ///     MUST NOT assign this.
    /// </summary>
    public SchemataJobExecution? Execution { get; set; }
}
