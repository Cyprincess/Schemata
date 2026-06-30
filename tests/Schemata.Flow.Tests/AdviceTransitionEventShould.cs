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
        Assert.Equal("flow:processes/p1:catch-msg", row.SubscriptionId);
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
                SubscriptionId = "flow:processes/p1:catch-msg",
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
                SubscriptionId = "flow:processes/p1:catch-msg",
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
        Assert.Contains(rows, r => r is { SubscriptionId: "flow:processes/p1:catch-pay", CorrelationKey: "processes/p1" });
        Assert.Contains(rows, r => r is { SubscriptionId: "flow:processes/p1:catch-sig", CorrelationKey: null });
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

    private static AdviceContext Advice() {
        return new(new ServiceCollection().BuildServiceProvider());
    }

    private static FlowTransitionContext Context(
        SchemataProcess   process,
        ProcessDefinition definition,
        string?           waitingAtName,
        string?           previousWaitingAtName = null
    ) {
        var token = new TokenSnapshot {
            CanonicalName = "processes/p1/tokens/t1",
            ScopeName     = "p1",
            StateName     = waitingAtName ?? "post-wait",
            WaitingAtName = waitingAtName,
            Status        = waitingAtName is null ? "Active" : "Waiting",
        };

        return new() {
            Definition            = definition,
            Snapshot              = new() { Process = process, Tokens = [], Transitions = [] },
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
