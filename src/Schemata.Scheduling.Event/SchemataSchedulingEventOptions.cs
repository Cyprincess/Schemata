using System;
using System.Collections.Generic;
using Schemata.Abstractions.Advisors;

namespace Schemata.Scheduling.Event;

/// <summary>Configures lifecycle event publishing for the Scheduling.Event feature.</summary>
public class SchemataSchedulingEventOptions
{
    /// <summary>Default publish gate applied when no per-job entry or attribute is found.</summary>
    public AdviseResult DefaultPublishEventResult { get; set; } = AdviseResult.Continue;

    /// <summary>Per-job overrides keyed by <see cref="Schemata.Scheduling.Skeleton.IScheduledJob" /> type.</summary>
    public Dictionary<Type, JobEventConfiguration> Jobs { get; } = new();
}
