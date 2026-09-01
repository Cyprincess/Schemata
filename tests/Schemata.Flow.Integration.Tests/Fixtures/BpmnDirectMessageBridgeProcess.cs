using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class BpmnDirectMessageBridgeProcess : ProcessDefinition
{
    public BpmnDirectMessageBridgeProcess() {
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var message = new Message { Name   = "direct-message" };
        var catchEvent = new FlowEvent {
            Name       = "message-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = message,
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, catchEvent, end]);
        Messages.Add(message);
        Flows.Add(new() { Source = start, Target      = catchEvent });
        Flows.Add(new() { Source = catchEvent, Target = end });
    }
}