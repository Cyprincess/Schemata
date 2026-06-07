using System.Collections.Generic;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Options controlling how the scheduler reacts to missed fires and which jobs it tracks.
/// </summary>
public class SchemataSchedulingOptions
{
    /// <summary>Registered jobs to materialize on startup.</summary>
    public List<JobRegistration> Jobs { get; } = new();

    /// <summary>
    ///     Policy applied when a job's <c>NextRunTime</c> is in the past at the
    ///     moment the scheduler observes it (typically right after a restart or
    ///     a paused->active transition).  Defaults to
    ///     <see cref="MissedFirePolicy.FireOnce" />.
    /// </summary>
    public MissedFirePolicy MissedFirePolicy { get; set; } = MissedFirePolicy.FireOnce;
}