using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class MessageGatewayBridgeProcess : ProcessDefinition
{
    public MessageGatewayBridgeProcess() {
        var start   = new FlowEvent { Name         = "start", Position = EventPosition.Start };
        var gateway = new EventBasedGateway { Name = "start-gateway" };
        var message = new Message { Name           = "start-message" };
        var catchEvent = new FlowEvent {
            Name       = "start-message",
            Position   = EventPosition.IntermediateCatch,
            Definition = message,
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, gateway, catchEvent, end]);
        Messages.Add(message);
        Flows.Add(new() { Source = start, Target      = gateway });
        Flows.Add(new() { Source = gateway, Target    = catchEvent });
        Flows.Add(new() { Source = catchEvent, Target = end });
    }
}