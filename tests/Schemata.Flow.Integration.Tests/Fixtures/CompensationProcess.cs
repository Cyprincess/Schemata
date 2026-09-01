using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public abstract class CompensationProcess : ProcessDefinition
{
    protected CompensationProcess(bool throwsCompensation) {
        var start     = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var host      = new NoneTask { Name  = "host" };
        var afterHost = new NoneTask { Name  = "after-host" };
        var after     = new NoneTask { Name  = "after" };
        var end       = new FlowEvent { Name = "end", Position = EventPosition.End };
        var boundary = new FlowEvent {
            Name       = "compensate-host",
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = new CompensationDefinition { Name = "compensate-host", Activity = host },
        };
        var undo = new NoneTask { Name = "undo-host" };

        Elements.AddRange([start, host, afterHost, after, end, boundary, undo]);
        Flows.Add(new() { Source = start, Target = host });
        Flows.Add(new() { Source = host, Target  = afterHost });

        if (throwsCompensation) {
            var throwEvent = new FlowEvent {
                Name       = "throw",
                Position   = EventPosition.IntermediateThrow,
                Definition = new CompensationDefinition { Name = "compensate" },
            };
            Elements.Add(throwEvent);
            Flows.Add(new() { Source = afterHost, Target  = throwEvent });
            Flows.Add(new() { Source = throwEvent, Target = after });
        } else {
            Flows.Add(new() { Source = afterHost, Target = after });
        }

        Flows.Add(new() { Source = after, Target    = end });
        Flows.Add(new() { Source = boundary, Target = undo });
    }
}