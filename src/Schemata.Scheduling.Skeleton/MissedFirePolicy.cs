namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Determines how the scheduler reacts to a missed fire window.
/// </summary>
public enum MissedFirePolicy
{
    /// <summary>Skip the missed fire and schedule the next future occurrence.</summary>
    Skip = 0,

    /// <summary>Fire once immediately, then schedule the next future occurrence. Default.</summary>
    FireOnce = 1,

    /// <summary>Replay every missed occurrence in sequence, then catch up.</summary>
    FireAll = 2,
}