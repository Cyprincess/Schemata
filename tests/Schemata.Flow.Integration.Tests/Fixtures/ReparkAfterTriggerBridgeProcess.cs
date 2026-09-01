using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class ReparkAfterTriggerBridgeProcess : ProcessDefinition
{
    public ReparkAfterTriggerBridgeProcess() {
        var start         = new FlowEvent { Name         = "start", Position = EventPosition.Start };
        var firstGateway  = new EventBasedGateway { Name = "first-gateway" };
        var secondGateway = new EventBasedGateway { Name = "second-gateway" };
        var first         = new Message { Name           = "first-message" };
        var second        = new Message { Name           = "second-message" };
        var firstCatch = new FlowEvent {
            Name       = "first-message-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = first,
        };
        var secondCatch = new FlowEvent {
            Name       = "second-message",
            Position   = EventPosition.IntermediateCatch,
            Definition = second,
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, firstGateway, firstCatch, secondGateway, secondCatch, end]);
        Messages.Add(first);
        Messages.Add(second);
        Flows.Add(new() { Source = start, Target         = firstGateway });
        Flows.Add(new() { Source = firstGateway, Target  = firstCatch });
        Flows.Add(new() { Source = firstCatch, Target    = secondGateway });
        Flows.Add(new() { Source = secondGateway, Target = secondCatch });
        Flows.Add(new() { Source = secondCatch, Target   = end });
    }
}