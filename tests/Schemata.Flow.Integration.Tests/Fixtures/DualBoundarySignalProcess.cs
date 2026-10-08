using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class DualBoundarySignalProcess : ProcessDefinition
{
    public const string SignalName = "dual-boundary-signal";

    public DualBoundarySignalProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var fork  = new ParallelGateway { Name = "fork" };
        var hostA = new UserTask { Name = "host-a" };
        var hostB = new UserTask { Name = "host-b" };
        var signal = new Signal { Name = SignalName };
        var boundaryA = new FlowEvent {
            Name        = "boundary-a",
            Position    = EventPosition.Boundary,
            AttachedTo  = hostA,
            Definition  = signal,
            Interrupting = true,
        };
        var boundaryB = new FlowEvent {
            Name        = "boundary-b",
            Position    = EventPosition.Boundary,
            AttachedTo  = hostB,
            Definition  = signal,
            Interrupting = true,
        };
        var afterA = new UserTask { Name = "after-a" };
        var end    = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, fork, hostA, hostB, boundaryA, boundaryB, afterA, end]);
        Signals.Add(signal);
        Flows.AddRange([
            new() { Source = start, Target = fork },
            new() { Source = fork, Target = hostA },
            new() { Source = fork, Target = hostB },
            new() { Source = hostA, Target = end },
            new() { Source = hostB, Target = end },
            new() { Source = boundaryA, Target = afterA },
            new() { Source = boundaryB, Target = end },
            new() { Source = afterA, Target = end },
        ]);
    }
}
