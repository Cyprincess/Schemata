using System;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Execution features declared by a flow runtime.</summary>
[Flags]
public enum FlowRuntimeCapabilities
{
    None = 0,
    ProcedureTasks = 1 << 0,
    MultiToken = 1 << 1,
    NestedEvents = 1 << 2,
    NestedTimers = 1 << 3,
    Compensation = 1 << 4,
    SubProcesses = 1 << 5,
    Loops = 1 << 6,
    NonInterruptingBoundaries = 1 << 7,
    All = ProcedureTasks | MultiToken | NestedEvents | NestedTimers | Compensation | SubProcesses | Loops | NonInterruptingBoundaries,
}
