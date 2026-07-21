using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Schemata.Abstractions.Advisors;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;

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
        var apply = new NoneTask { Name = "apply" };
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

public sealed class RecordingTransitionAdvisor : IFlowTransitionAdvisor
{
    public ConcurrentQueue<TransitionRecord> Observed { get; } = new();

    #region IFlowTransitionAdvisor Members

    public int Order => 100;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, FlowTransitionContext context, CancellationToken ct = default) {
        Observed.Enqueue(new(context.Snapshot.Process.CanonicalName!, context.Token.CanonicalName, context.PreviousWaitingAtName));
        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}

public sealed record TransitionRecord(string Process, string Token, string? PreviousWaitingAtName);
