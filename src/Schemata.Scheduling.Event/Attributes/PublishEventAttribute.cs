using System;
using Schemata.Abstractions.Advisors;

namespace Schemata.Scheduling.Event.Attributes;

/// <summary>
///     Declares per-job lifecycle event publishing behaviour for an <see cref="Schemata.Scheduling.Skeleton.IScheduledJob" />.
///     Resolved by <c>EventPublishingJobLifecycleObserver</c> when no entry exists in
///     <see cref="SchemataSchedulingEventOptions.Jobs" />.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PublishEventAttribute : Attribute
{
    /// <summary>Initializes the attribute with a publish gate and an execution-interception flag.</summary>
    public PublishEventAttribute(AdviseResult result = AdviseResult.Continue, bool interceptExecution = false) {
        Result             = result;
        InterceptExecution = interceptExecution;
    }

    /// <summary>Gate controlling whether lifecycle events are published for the job.</summary>
    public AdviseResult Result { get; }

    /// <summary>When <c>true</c>, the trigger hook returns <c>Skip</c> after publishing <c>JobTriggered</c>.</summary>
    public bool InterceptExecution { get; }
}
