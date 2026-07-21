using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Event.Internal;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class AdviceTransitionEventShould
{
    [Fact]
    public async SystemTask AddsSubscription_WhenEnteringMessageCatch() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var (definition, process) = MessageCatchSetup();

        await advisor.AdviseAsync(Advice(), Context(process, definition, "catch-msg"));

        var row = Assert.Single(rows);
        Assert.Equal("flow:processes/p1:catch-msg:processes/p1/tokens/t1", row.SubscriptionId);
        Assert.Equal("payment", row.EventType);
        Assert.Equal("processes/p1", row.CorrelationKey);
        Assert.Equal("processes/p1", row.Target);
    }

    [Fact]
    public async SystemTask AddsSubscription_WithNullCorrelation_WhenEnteringSignalCatch() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent {
            Name       = "catch-sig",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Signal { Name = "shutdown" },
        });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(Advice(), Context(process, definition, "catch-sig"));

        var row = Assert.Single(rows);
        Assert.Null(row.CorrelationKey);
        Assert.Equal("shutdown", row.EventType);
    }

    [Fact]
    public async SystemTask RemovesOldSubscription_WhenProcessReachesTerminalState() {
        var rows = new List<SchemataEventSubscription> {
            new() {
                SubscriptionId = "flow:processes/p1:catch-msg:processes/p1/tokens/t1",
                Token          = "processes/p1/tokens/t1",
                EventType      = "payment",
                CorrelationKey = "processes/p1",
                Target         = "processes/p1",
            },
        };
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var (definition, process) = MessageCatchSetup();
        process.State             = "Completed";

        await advisor.AdviseAsync(
            Advice(),
            Context(process, definition, null, "catch-msg"));

        Assert.Empty(rows);
    }

    [Fact]
    public async SystemTask UpsertsSubscription_WhenReenteringWithDifferentMetadata() {
        var rows = new List<SchemataEventSubscription> {
            new() {
                SubscriptionId = "flow:processes/p1:catch-msg:processes/p1/tokens/t1",
                Token          = "processes/p1/tokens/t1",
                EventType      = "stale-event",
                CorrelationKey = "stale-key",
                Target         = "stale-target",
            },
        };
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var (definition, process) = MessageCatchSetup();

        await advisor.AdviseAsync(Advice(), Context(process, definition, "catch-msg"));

        var row = Assert.Single(rows);
        Assert.Equal("payment", row.EventType);
        Assert.Equal("processes/p1", row.CorrelationKey);
        Assert.Equal("processes/p1", row.Target);
    }

    [Fact]
    public async SystemTask AddsSubscription_ForMessageCatchNestedInSubProcess() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var catchEvent = new FlowEvent {
            Name       = "payment-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Message { Name = "payment" },
        };
        var nested = new EmbeddedSubProcess { Name = "subprocess" };
        nested.Children.Add(catchEvent);
        var definition = new ProcessDefinition();
        definition.Elements.Add(nested);
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(Advice(), Context(process, definition, "payment-catch"));

        var row = Assert.Single(rows);
        Assert.Equal("processes/p1/tokens/t1", row.Token);
        Assert.Equal("flow:processes/p1:payment-catch:processes/p1/tokens/t1", row.SubscriptionId);
    }

    [Fact]
    public async SystemTask AddsSubscriptionPerBranch_WhenEnteringEventBasedGateway() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var pay = new FlowEvent {
            Name       = "catch-pay",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Message { Name = "payment" },
        };
        var shutdown = new FlowEvent {
            Name       = "catch-sig",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Signal { Name = "shutdown" },
        };
        var gateway = new EventBasedGateway { Name = "gw" };

        var definition = new ProcessDefinition();
        definition.Elements.Add(gateway);
        definition.Elements.Add(pay);
        definition.Elements.Add(shutdown);
        definition.Flows.Add(new() { Source = gateway, Target = pay });
        definition.Flows.Add(new() { Source = gateway, Target = shutdown });

        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(Advice(), Context(process, definition, "gw"));

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r is { SubscriptionId: "flow:processes/p1:catch-pay:processes/p1/tokens/t1", CorrelationKey: "processes/p1" });
        Assert.Contains(rows, r => r is { SubscriptionId: "flow:processes/p1:catch-sig:broadcast", CorrelationKey: null });
    }

    [Fact]
    public async SystemTask JoinsUnitOfWork_AndDoesNotCommit() {
        var rows       = new List<SchemataEventSubscription>();
        var repository = Repository(rows);
        var uow        = Mock.Of<IUnitOfWork>();
        var advisor    = new AdviceTransitionEvent(repository.Object);

        var (definition, process) = MessageCatchSetup();
        var context               = Context(process, definition, "catch-msg");
        context.UnitOfWork        = uow;

        await advisor.AdviseAsync(Advice(), context);

        repository.Verify(r => r.Join(uow), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async SystemTask AddsBoundarySubscription_WhenActiveAtHostActivity() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var (definition, host, boundary) = BoundarySetup(new Message { Name = "payment" });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(Advice(), Context(process, definition, null, stateName: host.Name));

        var row = Assert.Single(rows);
        Assert.Equal($"flow:processes/p1:{boundary.Name}:processes/p1/tokens/t1", row.SubscriptionId);
        Assert.Equal("payment", row.EventType);
        Assert.Equal("processes/p1", row.CorrelationKey);
        Assert.Equal("processes/p1", row.Target);
    }

    [Fact]
    public async SystemTask AddsBoundarySubscription_WithNullCorrelation_ForSignalCatch() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var (definition, host, boundary) = BoundarySetup(new Signal { Name = "shutdown" });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(Advice(), Context(process, definition, null, stateName: host.Name));

        var row = Assert.Single(rows);
        Assert.Equal($"flow:processes/p1:{boundary.Name}:broadcast", row.SubscriptionId);
        Assert.Null(row.CorrelationKey);
    }

    [Fact]
    public async SystemTask RemovesBoundarySubscription_WhenTokenLeavesHostActivity() {
        var rows = new List<SchemataEventSubscription> {
            new() {
                SubscriptionId = "flow:processes/p1:Catch_work_payment:processes/p1/tokens/t1",
                Token          = "processes/p1/tokens/t1",
                EventType      = "payment",
                CorrelationKey = "processes/p1",
                Target         = "processes/p1",
            },
        };
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var (definition, host, _) = BoundarySetup(new Message { Name = "payment" });
        var next = new UserTask { Name = "next" };
        definition.Elements.Add(next);
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(
            Advice(),
            Context(process, definition, null, stateName: next.Name, previousStateName: host.Name));

        Assert.Empty(rows);
    }

    [Fact]
    public async SystemTask DoesNotAddBoundarySubscription_WhenTokenNotActive() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var (definition, host, _) = BoundarySetup(new Message { Name = "payment" });
        var process = new SchemataProcess { CanonicalName = "processes/p1", State = "Completed" };

        await advisor.AdviseAsync(
            Advice(),
            Context(process, definition, null, stateName: host.Name, status: "Completed"));

        Assert.Empty(rows);
    }

    [Fact]
    public async SystemTask DoesNotAddBoundarySubscription_ForNonBusEventDefinitions() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new AdviceTransitionEvent(Repository(rows).Object);

        var (definition, host, _) = BoundarySetup(new ErrorDefinition { Name = "Boom" });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(Advice(), Context(process, definition, null, stateName: host.Name));

        Assert.Empty(rows);
    }

    private static AdviceContext Advice() {
        return new(new ServiceCollection().BuildServiceProvider());
    }

    private static FlowTransitionContext Context(
        SchemataProcess   process,
        ProcessDefinition definition,
        string?           waitingAtName,
        string?           previousWaitingAtName = null,
        string?           stateName             = null,
        string?           status                = null,
        string?           previousStateName     = null
    ) {
        var token = new TokenSnapshot {
            CanonicalName = "processes/p1/tokens/t1",
            ScopeName     = "p1",
            StateName     = stateName ?? waitingAtName ?? "post-wait",
            WaitingAtName = waitingAtName,
            Status        = status ?? (waitingAtName is null ? "Active" : "Waiting"),
        };

        SchemataProcessTransition[] transitions = [];
        if (previousStateName is not null) {
            transitions = [new() { Token = token.CanonicalName, Previous = previousStateName }];
        }

        return new() {
            Definition            = definition,
            Snapshot              = new() { Process = process, Tokens = [], Transitions = transitions },
            Token                 = token,
            PreviousWaitingAtName = previousWaitingAtName,
            UnitOfWork            = Mock.Of<IUnitOfWork>(),
        };
    }

    private static (ProcessDefinition Definition, SchemataProcess Process) MessageCatchSetup() {
        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent {
            Name       = "catch-msg",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Message { Name = "payment" },
        });
        return (definition, new() { CanonicalName = "processes/p1" });
    }

    private static (ProcessDefinition Definition, UserTask Host, FlowEvent Boundary) BoundarySetup(
        IEventDefinition eventDefinition
    ) {
        var host = new UserTask { Name = "work" };
        var boundary = new FlowEvent {
            Name       = "Catch_work_payment",
            Position   = EventPosition.Boundary,
            Definition = eventDefinition,
            AttachedTo = host,
        };

        var definition = new ProcessDefinition();
        definition.Elements.Add(host);
        definition.Elements.Add(boundary);
        return (definition, host, boundary);
    }

    private static Mock<IRepository<SchemataEventSubscription>> Repository(List<SchemataEventSubscription> rows) {
        var records = new Mock<IRepository<SchemataEventSubscription>>();
        records.Setup(r => r.AddAsync(It.IsAny<SchemataEventSubscription>(), It.IsAny<CancellationToken>()))
               .Callback((SchemataEventSubscription row, CancellationToken _) => rows.Add(row))
               .Returns(SystemTask.CompletedTask);
        records.Setup(r => r.UpdateAsync(It.IsAny<SchemataEventSubscription>(), It.IsAny<CancellationToken>()))
               .Returns(SystemTask.CompletedTask);
        records.Setup(r => r.RemoveAsync(It.IsAny<SchemataEventSubscription>(), It.IsAny<CancellationToken>()))
               .Callback((SchemataEventSubscription row, CancellationToken _) => rows.Remove(row))
               .Returns(SystemTask.CompletedTask);
        records.Setup(r => r.FirstOrDefaultAsync(
                          It.IsAny<Func<IQueryable<SchemataEventSubscription>,
                              IQueryable<SchemataEventSubscription>>>(), It.IsAny<CancellationToken>()))
               .Returns((
                            Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>
                                predicate,
                            CancellationToken _
                        ) => new(predicate(rows.AsQueryable()).FirstOrDefault()));
        return records;
    }
}
