using System;
using System.Collections.Generic;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Options controlling how the scheduler reacts to missed fires and which jobs it tracks.
/// </summary>
public class SchemataSchedulingOptions
{
    /// <summary>
    ///     Registered jobs. Entries with a <see cref="JobRegistration.Schedule" /> are materialized
    ///     and armed on startup; entries with a <see langword="null" /> schedule are keyed only, so a
    ///     job triggered on-demand via <c>IScheduler.TriggerAsync</c> resolves its stable key after a
    ///     restart.
    /// </summary>
    public List<JobRegistration> Jobs { get; } = [];

    /// <summary>
    ///     Policy applied when a job's <c>NextRunTime</c> is in the past at the
    ///     moment the scheduler observes it (typically right after a restart or
    ///     a paused->active transition).  Defaults to
    ///     <see cref="MissedFirePolicy.FireOnce" />.
    /// </summary>
    public MissedFirePolicy MissedFirePolicy { get; set; } = MissedFirePolicy.FireOnce;

    /// <summary>Interval between persisted operation state reads while waiting in-process.</summary>
    public TimeSpan OperationPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
}
