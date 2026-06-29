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
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class FlowEventTransitionAdvisorShould
{
    [Fact]
    public async SystemTask AddsSubscription_WhenEnteringMessageCatch() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new FlowEventTransitionAdvisor(Repository(rows).Object);

        var (definition, process) = MessageCatchSetup();

        await advisor.AdviseAsync(
            Advice(), new() {
                Process    = process,
                Definition = definition,
                Instance   = new() { WaitingAtId = "catch-msg" },
                UnitOfWork = Mock.Of<IUnitOfWork>(),
            });

        var row = Assert.Single(rows);
        Assert.Equal("flow:processes/p1:catch-msg", row.SubscriptionId);
        Assert.Equal("payment", row.EventType);
        Assert.Equal("processes/p1", row.CorrelationKey);
        Assert.Equal("processes/p1", row.Target);
    }

    [Fact]
    public async SystemTask AddsSubscription_WithNullCorrelation_WhenEnteringSignalCatch() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new FlowEventTransitionAdvisor(Repository(rows).Object);

        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent {
            Id         = "catch-sig",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Signal { Name = "shutdown" },
        });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(
            Advice(), new() {
                Process    = process,
                Definition = definition,
                Instance   = new() { WaitingAtId = "catch-sig" },
                UnitOfWork = Mock.Of<IUnitOfWork>(),
            });

        var row = Assert.Single(rows);
        Assert.Null(row.CorrelationKey);
        Assert.Equal("shutdown", row.EventType);
    }

    [Fact]
    public async SystemTask RemovesOldSubscription_WhenLeavingWaitingState() {
        var rows = new List<SchemataEventSubscription> {
            new() {
                SubscriptionId = "flow:processes/p1:catch-msg",
                EventType      = "payment",
                CorrelationKey = "processes/p1",
                Target         = "processes/p1",
            },
        };
        var advisor = new FlowEventTransitionAdvisor(Repository(rows).Object);

        var (definition, process) = MessageCatchSetup();

        await advisor.AdviseAsync(
            Advice(), new() {
                Process             = process,
                Definition          = definition,
                Instance            = new() { IsComplete = true },
                PreviousWaitingAtId = "catch-msg",
                UnitOfWork          = Mock.Of<IUnitOfWork>(),
            });

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
        var advisor = new FlowEventTransitionAdvisor(Repository(rows).Object);

        var (definition, process) = MessageCatchSetup();

        await advisor.AdviseAsync(
            Advice(), new() {
                Process    = process,
                Definition = definition,
                Instance   = new() { WaitingAtId = "catch-msg" },
                UnitOfWork = Mock.Of<IUnitOfWork>(),
            });

        var row = Assert.Single(rows);
        Assert.Equal("payment", row.EventType);
        Assert.Equal("processes/p1", row.CorrelationKey);
        Assert.Equal("processes/p1", row.Target);
    }

    [Fact]
    public async SystemTask AddsSubscriptionPerBranch_WhenEnteringEventBasedGateway() {
        var rows    = new List<SchemataEventSubscription>();
        var advisor = new FlowEventTransitionAdvisor(Repository(rows).Object);

        var pay = new FlowEvent {
            Id         = "catch-pay",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Message { Name = "payment" },
        };
        var shutdown = new FlowEvent {
            Id         = "catch-sig",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Signal { Name = "shutdown" },
        };
        var gateway = new EventBasedGateway { Id = "gw" };

        var definition = new ProcessDefinition();
        definition.Elements.Add(gateway);
        definition.Elements.Add(pay);
        definition.Elements.Add(shutdown);
        definition.Flows.Add(new() { Id = "f1", Source = gateway, Target = pay });
        definition.Flows.Add(new() { Id = "f2", Source = gateway, Target = shutdown });

        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.AdviseAsync(
            Advice(), new() {
                Process    = process,
                Definition = definition,
                Instance   = new() { WaitingAtId = "gw" },
                UnitOfWork = Mock.Of<IUnitOfWork>(),
            });

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.SubscriptionId == "flow:processes/p1:catch-pay" && r.CorrelationKey == "processes/p1");
        Assert.Contains(rows, r => r.SubscriptionId == "flow:processes/p1:catch-sig" && r.CorrelationKey == null);
    }

    [Fact]
    public async SystemTask JoinsUnitOfWork_AndDoesNotCommit() {
        var rows       = new List<SchemataEventSubscription>();
        var repository = Repository(rows);
        var uow        = Mock.Of<IUnitOfWork>();
        var advisor    = new FlowEventTransitionAdvisor(repository.Object);

        var (definition, process) = MessageCatchSetup();

        await advisor.AdviseAsync(
            Advice(), new() {
                Process    = process,
                Definition = definition,
                Instance   = new() { WaitingAtId = "catch-msg" },
                UnitOfWork = uow,
            });

        repository.Verify(r => r.Join(uow), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AdviceContext Advice() {
        return new(new ServiceCollection().BuildServiceProvider());
    }

    private static (ProcessDefinition Definition, SchemataProcess Process) MessageCatchSetup() {
        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent {
            Id         = "catch-msg",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Message { Name = "payment" },
        });
        return (definition, new SchemataProcess { CanonicalName = "processes/p1" });
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
