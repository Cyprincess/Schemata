using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class BoundarySignalProcess : ProcessDefinition
{
    public const string SignalName = "boundary-broadcast-signal";

    public BoundarySignalProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var host  = new UserTask { Name  = "host" };
        var signal = new Signal { Name = SignalName };
        var boundary = new FlowEvent {
            Name       = "boundary-signal",
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = signal,
        };
        var completed = new FlowEvent { Name = "completed", Position = EventPosition.End };
        var signaled  = new FlowEvent { Name = "signaled", Position = EventPosition.End };

        Elements.AddRange([start, host, boundary, completed, signaled]);
        Signals.Add(signal);
        Flows.Add(new() { Source = start, Target    = host });
        Flows.Add(new() { Source = host, Target     = completed });
        Flows.Add(new() { Source = boundary, Target = signaled });
    }
}
