using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Flow.Scheduling.Internal;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class FlowTimerObserverShould
{
    [Fact]
    public async SystemTask MultipleTimersOneInstance_DistinctJobNames() {
        var scheduled = new List<string>();
        var variables = new Dictionary<string, IReadOnlyDictionary<string, string?>?>();
        var scheduler = new Mock<IScheduler>();
        scheduler
           .Setup(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<IReadOnlyDictionary<string, string?>?>(),
                                       It.IsAny<CancellationToken>()))
           .Callback<SchemataJob, IReadOnlyDictionary<string, string?>?,
                CancellationToken>((job, vars, _) => {
                    scheduled.Add(job.Name!);
                    variables[job.Name!] = vars;
                })
           .Returns(SystemTask.CompletedTask);
        scheduler.Setup(s => s.UnscheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(SystemTask.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(scheduler.Object);
        var provider = services.BuildServiceProvider();
        var advisor  = new AdviceTransitionTimer(provider);
        var advice   = new AdviceContext(provider);

        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent {
            Name       = "timer-a",
            Position   = EventPosition.IntermediateCatch,
            Definition = new TimerDefinition { TimerType = TimerType.Duration, TimeExpression = "PT1H", Name = "t" },
        });
        definition.Elements.Add(new FlowEvent {
            Name       = "timer-b",
            Position   = EventPosition.IntermediateCatch,
            Definition = new TimerDefinition { TimerType = TimerType.Duration, TimeExpression = "PT2H", Name = "t" },
        });

        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(advice, Context(process, definition, "timer-a"));
        await advisor.AdviseAsync(advice, Context(process, definition, "timer-b"));

        Assert.Equal(2, scheduled.Count);
        Assert.Equal(2, scheduled.Distinct().Count());
        Assert.Contains("flow-p1-timer-a-t1", scheduled);
        Assert.Contains("flow-p1-timer-b-t1", scheduled);
        Assert.Equal("processes/p1/tokens/t1", variables["flow-p1-timer-a-t1"]!["tokenName"]);
        Assert.Equal("processes/p1/tokens/t1", variables["flow-p1-timer-b-t1"]!["tokenName"]);
    }

    [Fact]
    public async SystemTask SchedulesBoundaryTimer_ForHostNestedInSubProcess() {
        var jobs      = new List<SchemataJob>();
        var scheduler = new Mock<IScheduler>();
        scheduler
           .Setup(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<IReadOnlyDictionary<string, string?>?>(),
                                       It.IsAny<CancellationToken>()))
           .Callback<SchemataJob, IReadOnlyDictionary<string, string?>?, CancellationToken>((job, _, _) => jobs.Add(job))
           .Returns(SystemTask.CompletedTask);

        var services = new ServiceCollection().AddSingleton(scheduler.Object).BuildServiceProvider();
        var advisor  = new AdviceTransitionTimer(services);
        var advice   = new AdviceContext(services);

        var host = new UserTask { Name = "review" };
        var boundary = new FlowEvent {
            Name       = "review-timeout",
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = new TimerDefinition { Name = "timeout", TimerType = TimerType.Duration, TimeExpression = "PT1H" },
        };
        var nested = new EmbeddedSubProcess { Name = "subprocess" };
        nested.Children.Add(host);
        nested.Children.Add(boundary);
        var definition = new ProcessDefinition();
        definition.Elements.Add(nested);
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(advice, Context(process, definition, null, "review"));

        var scheduled = Assert.Single(jobs);
        Assert.Contains("review-timeout", scheduled.Name);
    }

    [Fact]
    public async SystemTask NonTimerTransition_WithoutScheduler_PassesThrough() {
        var provider = new ServiceCollection().BuildServiceProvider();
        var advisor  = new AdviceTransitionTimer(provider);
        var advice   = new AdviceContext(provider);

        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent { Name = "catch-msg", Position = EventPosition.IntermediateCatch });

        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        var result = await advisor.AdviseAsync(advice, Context(process, definition, "catch-msg"));

        Assert.Equal(AdviseResult.Continue, result);
    }

    private static FlowTransitionContext Context(
        SchemataProcess   process,
        ProcessDefinition definition,
        string?           waitingAtName,
        string?           stateName = null
    ) {
        var token = new TokenSnapshot {
            CanonicalName = "processes/p1/tokens/t1",
            ScopeName     = "p1",
            StateName     = stateName ?? waitingAtName ?? "post-wait",
            WaitingAtName = waitingAtName,
            Status        = waitingAtName is null ? "Active" : "Waiting",
        };

        return new() {
            Definition = definition,
            Snapshot   = new() { Process = process, Tokens = [], Transitions = [] },
            Token      = token,
        };
    }
}
