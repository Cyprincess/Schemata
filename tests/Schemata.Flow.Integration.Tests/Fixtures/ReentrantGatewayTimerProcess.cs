using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class ReentrantGatewayTimerProcess : ProcessDefinition
{
    public ReentrantGatewayTimerProcess() {
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway = new EventBasedGateway { Name = "gateway" };
        var redo    = new Message { Name = "redo-message" };
        var redoCatch = new FlowEvent {
            Name       = "redo-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = redo,
        };
        var timerCatch = new FlowEvent {
            Name       = "timer-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = BridgeDefinitionHelpers.Timer("reentrant-timer"),
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, gateway, redoCatch, timerCatch, end]);
        Messages.Add(redo);
        Flows.Add(new() { Source = start, Target      = gateway });
        Flows.Add(new() { Source = gateway, Target    = redoCatch });
        Flows.Add(new() { Source = gateway, Target    = timerCatch });
        Flows.Add(new() { Source = redoCatch, Target  = gateway });
        Flows.Add(new() { Source = timerCatch, Target = end });
    }
}
