using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class MixedGatewayBridgeProcess : ProcessDefinition
{
    public MixedGatewayBridgeProcess() {
        var start   = new FlowEvent { Name         = "start", Position = EventPosition.Start };
        var gateway = new EventBasedGateway { Name = "gateway" };
        var message = new Message { Name           = "mixed-message" };
        var messageCatch = new FlowEvent {
            Name       = "message-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = message,
        };
        var timerCatch = new FlowEvent {
            Name       = "timer-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = BridgeDefinitionHelpers.Timer("mixed-timer"),
        };
        var messageEnd = new FlowEvent { Name = "message-end", Position = EventPosition.End };
        var timerEnd   = new FlowEvent { Name = "timer-end", Position   = EventPosition.End };

        Elements.AddRange([start, gateway, messageCatch, timerCatch, messageEnd, timerEnd]);
        Messages.Add(message);
        Flows.Add(new() { Source = start, Target        = gateway });
        Flows.Add(new() { Source = gateway, Target      = messageCatch });
        Flows.Add(new() { Source = gateway, Target      = timerCatch });
        Flows.Add(new() { Source = messageCatch, Target = messageEnd });
        Flows.Add(new() { Source = timerCatch, Target   = timerEnd });
    }
}