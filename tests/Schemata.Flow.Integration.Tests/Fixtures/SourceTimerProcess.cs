using System;
using System.Xml;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class SourceTimerProcess : ProcessDefinition
{
    public SourceTimerProcess() {
        BindSource<Order>(order => order.State);

        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var timer = new FlowEvent {
            Name     = "wait",
            Position = EventPosition.IntermediateCatch,
            Definition = new TimerDefinition {
                Name           = "source-timer",
                TimerType      = TimerType.Duration,
                TimeExpression = XmlConvert.ToString(TimeSpan.FromHours(1)),
            },
        };
        var apply = new NoneTask { Name  = "apply" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.Add(start);
        Elements.Add(timer);
        Elements.Add(apply);
        Elements.Add(end);

        Flows.Add(new() { Source = start, Target = timer });
        Flows.Add(new() { Source = timer, Target = apply });
        Flows.Add(new() { Source = apply, Target = end });
    }
}