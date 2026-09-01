using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class BoundaryTimerBridgeProcess : ProcessDefinition
{
    public BoundaryTimerBridgeProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var host  = new UserTask { Name  = "host" };
        var boundary = new FlowEvent {
            Name       = "boundary-timer",
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = BridgeDefinitionHelpers.Timer("boundary-timer"),
        };
        var completed = new FlowEvent { Name = "completed", Position = EventPosition.End };
        var timedOut  = new FlowEvent { Name = "timed-out", Position = EventPosition.End };

        Elements.AddRange([start, host, boundary, completed, timedOut]);
        Flows.Add(new() { Source = start, Target    = host });
        Flows.Add(new() { Source = host, Target     = completed });
        Flows.Add(new() { Source = boundary, Target = timedOut });
    }
}