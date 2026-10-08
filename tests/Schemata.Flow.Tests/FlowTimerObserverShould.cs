using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Flow.Scheduling.Handlers;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Scheduling.Foundation.Runtime;
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
                    scheduled.Add(job.Key!);
                    variables[job.Key!] = vars;
                })
           .Returns(SystemTask.CompletedTask);
        scheduler.Setup(s => s.UnscheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(SystemTask.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(scheduler.Object);
        services.AddSingleton<IScheduledJobRegistry, DefaultScheduledJobRegistry>();
        var provider = services.BuildServiceProvider();
        var handler = new FlowTimerCatchHandler(provider);

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

        await handler.ArmAsync(Context(process, definition, "timer-a"));
        await handler.ArmAsync(Context(process, definition, "timer-b"));

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

        var services = new ServiceCollection().AddSingleton(scheduler.Object)
                                              .AddSingleton<IScheduledJobRegistry, DefaultScheduledJobRegistry>()
                                              .BuildServiceProvider();
        var handler = new FlowTimerCatchHandler(services);

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

        await handler.ArmAsync(Context(process, definition, null, "review"));

        var scheduled = Assert.Single(jobs);
        Assert.Contains("review-timeout", scheduled.Key);
    }
    [Fact]
    public async SystemTask ArmsOutgoingTimerCatch_WhenTokenWaitsAtEventGateway() {
        var scheduled = new List<SchemataJob>();
        var variables = new Dictionary<string, IReadOnlyDictionary<string, string?>?>();
        var scheduler = new Mock<IScheduler>();
        scheduler
           .Setup(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<IReadOnlyDictionary<string, string?>?>(),
                                       It.IsAny<CancellationToken>()))
           .Callback<SchemataJob, IReadOnlyDictionary<string, string?>?, CancellationToken>((job, vars, _) => {
                scheduled.Add(job);
                variables[job.Key!] = vars;
            })
           .Returns(SystemTask.CompletedTask);

        var services = new ServiceCollection().AddSingleton(scheduler.Object)
                                              .AddSingleton<IScheduledJobRegistry, DefaultScheduledJobRegistry>()
                                              .BuildServiceProvider();
        var handler = new FlowTimerCatchHandler(services);

        var gateway = new EventBasedGateway { Name = "wait-gw" };
        var timer = new FlowEvent {
            Name       = "review-deadline",
            Position   = EventPosition.IntermediateCatch,
            Definition = new TimerDefinition { Name = "t", TimerType = TimerType.Duration, TimeExpression = "PT1H" },
        };
        var message = new FlowEvent { Name = "reviewed", Position = EventPosition.IntermediateCatch };
        var definition = new ProcessDefinition();
        definition.Elements.Add(gateway);
        definition.Elements.Add(timer);
        definition.Elements.Add(message);
        definition.Flows.Add(new() { Source = gateway, Target = timer });
        definition.Flows.Add(new() { Source = gateway, Target = message });

        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await handler.ArmAsync(Context(process, definition, "wait-gw"));

        var job = Assert.Single(scheduled);
        Assert.Equal("flow-p1-review-deadline-t1", job.Key);
        Assert.Equal("processes/p1/tokens/t1", variables[job.Key!]!["tokenName"]);
    }

    [Fact]
    public async SystemTask UnschedulesOutgoingTimerCatch_WhenTokenLeavesEventGateway() {
        var unscheduled = new List<string>();
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.UnscheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Callback<string, CancellationToken>((name, _) => unscheduled.Add(name))
                 .Returns(SystemTask.CompletedTask);

        var jobs = new Mock<Schemata.Entity.Repository.IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> query, CancellationToken _) =>
                 query(new[] { new SchemataJob {
                     Key          = "flow-p1-review-deadline-t1",
                     CanonicalName = "jobs/flow-p1-review-deadline-t1",
                 } }.AsQueryable()).FirstOrDefault());

        var services = new ServiceCollection().AddSingleton(scheduler.Object)
                                              .AddSingleton(jobs.Object)
                                              .AddSingleton<IScheduledJobRegistry, DefaultScheduledJobRegistry>()
                                              .BuildServiceProvider();
        var handler = new FlowTimerCatchHandler(services);

        var gateway = new EventBasedGateway { Name = "wait-gw" };
        var timer = new FlowEvent {
            Name       = "review-deadline",
            Position   = EventPosition.IntermediateCatch,
            Definition = new TimerDefinition { Name = "t", TimerType = TimerType.Duration, TimeExpression = "PT1H" },
        };
        var definition = new ProcessDefinition();
        definition.Elements.Add(gateway);
        definition.Elements.Add(timer);
        definition.Flows.Add(new() { Source = gateway, Target = timer });

        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await handler.ArmAsync(Context(process, definition, "reviewed", previousWaitingAtName: "wait-gw"));

        Assert.Equal(["jobs/flow-p1-review-deadline-t1"], unscheduled);
    }


    [Fact]
    public async SystemTask NonTimerTransition_WithoutScheduler_PassesThrough() {
        var provider = new ServiceCollection().BuildServiceProvider();
        var handler  = new FlowTimerCatchHandler(provider);

        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent { Name = "catch-msg", Position = EventPosition.IntermediateCatch });

        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        // A transition touching no timer catch must not reach the scheduler, so its absence is fine.
        var exception = await Record.ExceptionAsync(
            async () => await handler.ArmAsync(Context(process, definition, "catch-msg")));

        Assert.Null(exception);
    }

    [Fact]
    public async SystemTask UnschedulesBoundaryTimer_WhenActiveHostIsTerminated() {
        var unscheduled = new List<string>();
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.UnscheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Callback<string, CancellationToken>((name, _) => unscheduled.Add(name))
                 .Returns(SystemTask.CompletedTask);

        var jobs = new Mock<Schemata.Entity.Repository.IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> query, CancellationToken _) =>
                 query(new[] { new SchemataJob {
                     Key           = "flow-p1-boundary-t1",
                     CanonicalName = "jobs/flow-p1-boundary-t1",
                 } }.AsQueryable()).FirstOrDefault());

        var services = new ServiceCollection().AddSingleton(scheduler.Object)
                                              .AddSingleton(jobs.Object)
                                              .AddSingleton<IScheduledJobRegistry, DefaultScheduledJobRegistry>()
                                              .BuildServiceProvider();
        var handler = new FlowTimerCatchHandler(services);

        var host = new UserTask { Name = "host" };
        var boundary = new FlowEvent {
            Name       = "boundary",
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = new TimerDefinition { Name = "t", TimerType = TimerType.Duration, TimeExpression = "PT1H" },
        };
        var definition = new ProcessDefinition();
        definition.Elements.Add(host);
        definition.Elements.Add(boundary);

        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        // Termination keeps the token on its host element but leaves the Active state.
        await handler.ArmAsync(Context(process, definition, null, stateName: "host", previousStateName: "host",
                                       status: "Cancelled"));

        Assert.Equal(["jobs/flow-p1-boundary-t1"], unscheduled);
    }

    [Fact]
    public async SystemTask KeepsBoundaryTimer_WhenHostRemainsActive() {
        var unscheduled = new List<string>();
        var scheduler   = new Mock<IScheduler>();
        scheduler.Setup(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<IReadOnlyDictionary<string, string?>?>(),
                                             It.IsAny<CancellationToken>()))
                 .Returns(SystemTask.CompletedTask);
        scheduler.Setup(s => s.UnscheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Callback<string, CancellationToken>((name, _) => unscheduled.Add(name))
                 .Returns(SystemTask.CompletedTask);

        var services = new ServiceCollection().AddSingleton(scheduler.Object)
                                              .AddSingleton<IScheduledJobRegistry, DefaultScheduledJobRegistry>()
                                              .BuildServiceProvider();
        var handler = new FlowTimerCatchHandler(services);

        var host = new UserTask { Name = "host" };
        var boundary = new FlowEvent {
            Name       = "boundary",
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = new TimerDefinition { Name = "t", TimerType = TimerType.Duration, TimeExpression = "PT1H" },
        };
        var definition = new ProcessDefinition();
        definition.Elements.Add(host);
        definition.Elements.Add(boundary);

        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await handler.ArmAsync(Context(process, definition, null, stateName: "host", previousStateName: "host"));

        Assert.Empty(unscheduled);
    }

    private static FlowTransitionContext Context(
        SchemataProcess   process,
        ProcessDefinition definition,
        string?           waitingAtName,
        string?           stateName             = null,
        string?           previousWaitingAtName = null,
        string?           previousStateName     = null,
        string?           status                = null
    ) {
        var token = new TokenSnapshot {
            CanonicalName = "processes/p1/tokens/t1",
            ScopeName     = "p1",
            StateName     = stateName ?? waitingAtName ?? "post-wait",
            WaitingAtName = waitingAtName,
            Status        = status ?? (waitingAtName is null ? "Active" : "Waiting"),
        };

        return new() {
            Definition = definition,
            Snapshot = new() {
                Process = process,
                Tokens  = [],
                Transitions = previousStateName is null
                    ? []
                    : [new SchemataProcessTransition { Token = token.CanonicalName, Previous = previousStateName }],
            },
            Token                 = token,
            PreviousWaitingAtName = previousWaitingAtName,
        };
    }
}
