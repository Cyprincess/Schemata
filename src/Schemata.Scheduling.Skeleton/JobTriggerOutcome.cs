namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Outcome of <see cref="IJobLifecycleObserver.OnTriggeredAsync" />.  Most
///     restrictive wins: <c>Block &gt; Skip &gt; Proceed</c>.
/// </summary>
public enum JobTriggerOutcome
{
    /// <summary>Run the job.</summary>
    Proceed = 0,

    /// <summary>Skip this fire and advance the schedule.</summary>
    Skip = 1,

    /// <summary>Skip this fire and freeze the schedule.</summary>
    Block = 2,
}