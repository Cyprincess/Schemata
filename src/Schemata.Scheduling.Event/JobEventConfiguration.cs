using Schemata.Abstractions.Advisors;

namespace Schemata.Scheduling.Event;

/// <summary>Per-job lifecycle event publishing override stored under <see cref="SchemataSchedulingEventOptions.Jobs" />.</summary>
public sealed class JobEventConfiguration
{
    /// <summary>Gate controlling whether lifecycle events are published for the job.</summary>
    public AdviseResult Result { get; set; } = AdviseResult.Continue;

    /// <summary>When <c>true</c>, the trigger hook returns <c>Skip</c> after publishing <c>JobTriggered</c>.</summary>
    public bool InterceptExecution { get; set; }
}
