using System.Collections.Generic;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton;

/// <summary>
///     Options for the Schemata flow module.
/// </summary>
public class SchemataFlowOptions
{
    /// <summary>Marker written by the Flow.Event bridge.</summary>
    public const string EventsBridge = "events";

    /// <summary>Marker written by the Flow.Scheduling bridge.</summary>
    public const string TimersBridge = "timers";

    /// <summary>Runtime bridges activated for Flow catch events.</summary>
    public ISet<string> Bridges { get; set; } = new HashSet<string>();

    /// <summary>
    ///     Process configurations registered with the flow system.
    /// </summary>
    public List<ProcessConfiguration> Configurations { get; set; } = [];
}
