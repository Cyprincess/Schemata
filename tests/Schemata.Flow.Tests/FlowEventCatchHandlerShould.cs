using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Flow.Skeleton.Runtime;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Event.Handlers;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class FlowEventCatchHandlerShould
{
    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask AddsSubscription_WhenEnteringMessageCatch() {
        var rows    = new List<SchemataEventSubscription>();
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var (definition, process) = MessageCatchSetup();

        await advisor.ArmAsync(Context(services, process, definition, "catch-msg"));

        var row = Assert.Single(rows);
        Assert.Equal("flow:processes/p1:catch-msg:processes/p1/tokens/t1", row.SubscriptionId);
        Assert.Equal("payment", row.EventType);
        Assert.Equal("processes/p1", row.CorrelationKey);
        Assert.Equal("processes/p1", row.Target);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask AddsSubscription_WithNullCorrelation_WhenEnteringSignalCatch() {
        var rows    = new List<SchemataEventSubscription>();
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent {
            Name       = "catch-sig",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Signal { Name = "shutdown" },
        });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.ArmAsync(Context(services, process, definition, "catch-sig"));

        var row = Assert.Single(rows);
        Assert.Null(row.CorrelationKey);
        Assert.Equal("shutdown", row.EventType);
    }

    [Trait("Layer", "Unit")]
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
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var (definition, process) = MessageCatchSetup();
        process.State             = "Completed";

        await advisor.ArmAsync(Context(services, process, definition, null, "catch-msg"));

        Assert.Empty(rows);
    }

    [Trait("Layer", "Unit")]
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
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var (definition, process) = MessageCatchSetup();

        await advisor.ArmAsync(Context(services, process, definition, "catch-msg"));

        var row = Assert.Single(rows);
        Assert.Equal("payment", row.EventType);
        Assert.Equal("processes/p1", row.CorrelationKey);
        Assert.Equal("processes/p1", row.Target);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask AddsSubscription_ForMessageCatchNestedInSubProcess() {
        var rows    = new List<SchemataEventSubscription>();
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

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

        await advisor.ArmAsync(Context(services, process, definition, "payment-catch"));

        var row = Assert.Single(rows);
        Assert.Equal("processes/p1/tokens/t1", row.Token);
        Assert.Equal("flow:processes/p1:payment-catch:processes/p1/tokens/t1", row.SubscriptionId);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask AddsSubscriptionPerBranch_WhenEnteringEventBasedGateway() {
        var rows    = new List<SchemataEventSubscription>();
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

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

        await advisor.ArmAsync(Context(services, process, definition, "gw"));

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r is { SubscriptionId: "flow:processes/p1:catch-pay:processes/p1/tokens/t1", CorrelationKey: "processes/p1" });
        Assert.Contains(rows, r => r is { SubscriptionId: "flow:processes/p1:catch-sig:broadcast", CorrelationKey: null });
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask StagesWritesOnTheOuterUnitOfWork_AndDoesNotCommit() {
        var rows       = new List<SchemataEventSubscription>();
        var repository = Repository(rows);
        var mutation   = Mutation(rows);
        var uow        = Mock.Of<IUnitOfWork>();
        using var services = Provider(repository.Object, mutation.Object);
        var advisor = new FlowEventCatchHandler();

        var (definition, process) = MessageCatchSetup();
        var context               = Context(services, process, definition, "catch-msg");
        context.UnitOfWork        = uow;

        await advisor.ArmAsync(context);

        repository.Verify(r => r.Join(uow), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        mutation.Verify(
            m => m.CreateAsync(It.IsAny<SchemataEventSubscription>(), uow, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask AddsBoundarySubscription_WhenActiveAtHostActivity() {
        var rows    = new List<SchemataEventSubscription>();
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var (definition, host, boundary) = BoundarySetup(new Message { Name = "payment" });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.ArmAsync(Context(services, process, definition, null, stateName: host.Name));

        var row = Assert.Single(rows);
        Assert.Equal($"flow:processes/p1:{boundary.Name}:processes/p1/tokens/t1", row.SubscriptionId);
        Assert.Equal("payment", row.EventType);
        Assert.Equal("processes/p1", row.CorrelationKey);
        Assert.Equal("processes/p1", row.Target);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask AddsBoundarySubscription_WithNullCorrelation_ForSignalCatch() {
        var rows    = new List<SchemataEventSubscription>();
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var (definition, host, boundary) = BoundarySetup(new Signal { Name = "shutdown" });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.ArmAsync(Context(services, process, definition, null, stateName: host.Name));

        var row = Assert.Single(rows);
        Assert.Equal($"flow:processes/p1:{boundary.Name}:broadcast", row.SubscriptionId);
        Assert.Null(row.CorrelationKey);
    }

    [Trait("Layer", "Unit")]
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
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var (definition, host, _) = BoundarySetup(new Message { Name = "payment" });
        var next = new UserTask { Name = "next" };
        definition.Elements.Add(next);
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.ArmAsync(Context(services, process, definition, null, stateName: next.Name, previousStateName: host.Name));

        Assert.Empty(rows);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask DoesNotAddBoundarySubscription_WhenTokenNotActive() {
        var rows    = new List<SchemataEventSubscription>();
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var (definition, host, _) = BoundarySetup(new Message { Name = "payment" });
        var process = new SchemataProcess { CanonicalName = "processes/p1", State = "Completed" };

        await advisor.ArmAsync(Context(services, process, definition, null, stateName: host.Name, status: "Completed"));

        Assert.Empty(rows);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask DoesNotAddBoundarySubscription_ForNonBusEventDefinitions() {
        var rows    = new List<SchemataEventSubscription>();
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var (definition, host, _) = BoundarySetup(new ErrorDefinition { Name = "Boom" });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.ArmAsync(Context(services, process, definition, null, stateName: host.Name));

        Assert.Empty(rows);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask RemovesBroadcastSignalSubscription_WhenWaitingCatchLeaves() {
        var rows = new List<SchemataEventSubscription> {
            new() {
                SubscriptionId = "flow:processes/p1:catch-sig:broadcast",
                Token          = null,
                EventType      = "shutdown",
                Target         = "processes/p1",
            },
        };
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent {
            Name       = "catch-sig",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Signal { Name = "shutdown" },
        });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.ArmAsync(Context(services, process, definition, "next", previousWaitingAtName: "catch-sig"));

        Assert.Empty(rows);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask KeepsBroadcastSignalSubscription_WhenAnotherTokenStillWaits() {
        var rows = new List<SchemataEventSubscription> {
            new() {
                SubscriptionId = "flow:processes/p1:catch-sig:broadcast",
                Token          = null,
                EventType      = "shutdown",
                Target         = "processes/p1",
            },
        };
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent {
            Name       = "catch-sig",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Signal { Name = "shutdown" },
        });
        var process = new SchemataProcess { CanonicalName = "processes/p1" };
        var sibling = new SchemataProcessToken {
            Process       = "p1",
            CanonicalName = "processes/p1/tokens/t2",
            StateName     = "catch-sig",
            WaitingAtName = "catch-sig",
            State         = "Waiting",
        };

        await advisor.ArmAsync(Context(services, process, definition, "next", previousWaitingAtName: "catch-sig", otherTokens: [sibling]));

        Assert.Single(rows);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask RemovesBoundarySignalSubscription_WhenLastOwnerLeavesHost() {
        var rows = new List<SchemataEventSubscription> {
            new() {
                SubscriptionId = "flow:processes/p1:Catch_work_payment:broadcast",
                Token          = null,
                EventType      = "shutdown",
                Target         = "processes/p1",
            },
        };
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var (definition, host, _) = BoundarySetup(new Signal { Name = "shutdown" });
        var next = new UserTask { Name = "next" };
        definition.Elements.Add(next);
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.ArmAsync(Context(services, process, definition, null, stateName: next.Name, previousStateName: host.Name));

        Assert.Empty(rows);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask KeepsBroadcastSignalSubscription_WhenSiblingWaitsAtSameGateway() {
        var rows = new List<SchemataEventSubscription> {
            new() {
                SubscriptionId = "flow:processes/p1:catch-sig:broadcast",
                Token          = null,
                EventType      = "shutdown",
                Target         = "processes/p1",
            },
        };
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var definition = GatewaySignalDefinition();
        var process = new SchemataProcess { CanonicalName = "processes/p1" };
        var sibling = new SchemataProcessToken {
            Process       = "p1",
            CanonicalName = "processes/p1/tokens/t2",
            StateName     = "gw",
            WaitingAtName = "gw",
            State         = "Waiting",
        };

        await advisor.ArmAsync(Context(services, process, definition, "next", previousWaitingAtName: "gw", otherTokens: [sibling]));

        Assert.Single(rows);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async SystemTask RemovesBroadcastSignalSubscription_WhenLastGatewayWaiterLeaves() {
        var rows = new List<SchemataEventSubscription> {
            new() {
                SubscriptionId = "flow:processes/p1:catch-sig:broadcast",
                Token          = null,
                EventType      = "shutdown",
                Target         = "processes/p1",
            },
        };
        using var services = Provider(Repository(rows).Object, Mutation(rows).Object);
        var advisor = new FlowEventCatchHandler();

        var definition = GatewaySignalDefinition();
        var process = new SchemataProcess { CanonicalName = "processes/p1" };

        await advisor.ArmAsync(Context(services, process, definition, "next", previousWaitingAtName: "gw"));

        Assert.Empty(rows);
    }

    private static ProcessDefinition GatewaySignalDefinition() {
        var gateway = new EventBasedGateway { Name = "gw" };
        var catchEvent = new FlowEvent {
            Name       = "catch-sig",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Signal { Name = "shutdown" },
        };
        var definition = new ProcessDefinition();
        definition.Elements.Add(gateway);
        definition.Elements.Add(catchEvent);
        definition.Flows.Add(new() { Source = gateway, Target = catchEvent });
        return definition;
    }

    private static ServiceProvider Provider(IRepository<SchemataEventSubscription> repository,
        IResourceMutation<SchemataEventSubscription> mutation) => new ServiceCollection()
        .AddSingleton(repository).AddSingleton(mutation).BuildServiceProvider();

    private static FlowTransitionContext Context(
        IServiceProvider services,
        SchemataProcess   process,
        ProcessDefinition definition,
        string?           waitingAtName,
        string?           previousWaitingAtName = null,
        string?           stateName             = null,
        string?           status                = null,
        string?           previousStateName     = null,
        IReadOnlyList<SchemataProcessToken>? otherTokens = null
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

        var unit = Mock.Of<IUnitOfWork>();
        var execution = new FlowExecutionContext(unit, services) {
            CreateProcessAsync = (_, _) => throw new InvalidOperationException("Unexpected process creation."),
            CreateTokenAsync = (_, _) => throw new InvalidOperationException("Unexpected token creation."),
            PersistSnapshotAsync = (_, _) => throw new InvalidOperationException("Unexpected snapshot persistence."),
        };
        return new() {
            Definition            = definition,
            Snapshot              = new() { Process = process, Tokens = [..otherTokens ?? []], Transitions = transitions },
            Token                 = token,
            PreviousWaitingAtName = previousWaitingAtName,
            UnitOfWork            = unit,
            Execution             = execution,
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

    private static Mock<IResourceMutation<SchemataEventSubscription>> Mutation(List<SchemataEventSubscription> rows) {
        var mutation = new Mock<IResourceMutation<SchemataEventSubscription>>();
        mutation.Setup(m => m.CreateAsync(
                           It.IsAny<SchemataEventSubscription>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
                .Callback((SchemataEventSubscription row, IUnitOfWork? _, CancellationToken _) => rows.Add(row))
                .ReturnsAsync(MutationResult.Applied);
        mutation.Setup(m => m.UpdateAsync(
                           It.IsAny<SchemataEventSubscription>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(),
                           It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        mutation.Setup(m => m.DeleteAsync(
                           It.IsAny<SchemataEventSubscription>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(),
                           It.IsAny<CancellationToken>()))
                .Callback((SchemataEventSubscription row, IUnitOfWork? _, Operations _, CancellationToken _) => rows.Remove(row))
                .ReturnsAsync(MutationResult.Applied);
        return mutation;
    }
}
