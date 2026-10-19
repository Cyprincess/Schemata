using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class TerminateEndTimerProcess : ProcessDefinition
{
    public TerminateEndTimerProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var fork  = new ParallelGateway { Name = "fork" };
        var work  = new NoneTask { Name = "work" };
        var stop  = new FlowEvent { Name = "stop", Position = EventPosition.End, IsTerminate = true };
        var timerCatch = new FlowEvent {
            Name       = "timer-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = BridgeDefinitionHelpers.Timer("terminate-end-timer"),
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, fork, work, stop, timerCatch, end]);
        Flows.Add(new() { Source = start, Target      = fork });
        Flows.Add(new() { Source = fork, Target       = work });
        Flows.Add(new() { Source = work, Target       = stop });
        Flows.Add(new() { Source = fork, Target       = timerCatch });
        Flows.Add(new() { Source = timerCatch, Target = end });
    }
}
