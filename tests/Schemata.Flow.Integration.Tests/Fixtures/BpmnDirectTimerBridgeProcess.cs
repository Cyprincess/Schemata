using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class BpmnDirectTimerBridgeProcess : ProcessDefinition
{
    public BpmnDirectTimerBridgeProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var catchEvent = new FlowEvent {
            Name       = "timer-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = BridgeDefinitionHelpers.Timer("direct-timer"),
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, catchEvent, end]);
        Flows.Add(new() { Source = start, Target      = catchEvent });
        Flows.Add(new() { Source = catchEvent, Target = end });
    }
}