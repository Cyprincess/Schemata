using System;
using System.Xml;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class ParallelTimerProcess : ProcessDefinition
{
    public ParallelTimerProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var fork  = new ParallelGateway { Name = "fork" };
        var timerA = new FlowEvent {
            Name       = "timer-a",
            Position   = EventPosition.IntermediateCatch,
            Definition = Timer("parallel-timer-a"),
        };
        var timerB = new FlowEvent {
            Name       = "timer-b",
            Position   = EventPosition.IntermediateCatch,
            Definition = Timer("parallel-timer-b"),
        };
        var taskA = new NoneTask { Name = "task-a" };
        var taskB = new NoneTask { Name = "task-b" };
        var endA  = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB  = new FlowEvent { Name = "end-b", Position = EventPosition.End };

        Elements.Add(start);
        Elements.Add(fork);
        Elements.Add(timerA);
        Elements.Add(timerB);
        Elements.Add(taskA);
        Elements.Add(taskB);
        Elements.Add(endA);
        Elements.Add(endB);

        Flows.Add(new() { Source = start, Target = fork });
        Flows.Add(new() { Source = fork, Target = timerA });
        Flows.Add(new() { Source = fork, Target = timerB });
        Flows.Add(new() { Source = timerA, Target = taskA });
        Flows.Add(new() { Source = timerB, Target = taskB });
        Flows.Add(new() { Source = taskA, Target = endA });
        Flows.Add(new() { Source = taskB, Target = endB });
    }

    private static TimerDefinition Timer(string name) {
        return new() {
            Name           = name,
            TimerType      = TimerType.Duration,
            TimeExpression = XmlConvert.ToString(TimeSpan.FromHours(1)),
        };
    }
}