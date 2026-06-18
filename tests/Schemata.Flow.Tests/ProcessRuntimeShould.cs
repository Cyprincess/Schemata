using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class ProcessRuntimeShould
{
    [Fact]
    public async SystemTask StartProcessInstanceAsync_PersistsProcessAndTransitionInOneUnitOfWork() {
        var fixture = new RuntimeFixture();

        await fixture.Runtime.StartProcessInstanceAsync("approval");

        var uow = Assert.Single(fixture.UnitOfWorks);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Processes.Verify(r => r.Begin(), Times.Once);
        fixture.Transitions.Verify(r => r.Join(uow.Object), Times.Once);
        fixture.Processes.Verify(
            r => r.AddAsync(It.IsAny<SchemataProcess>(), It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Transitions.Verify(
            r => r.AddAsync(It.Is<SchemataProcessTransition>(t => t.Event == "Start"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async SystemTask StartProcessInstanceAsync_TransitionStoresParentProcessName() {
        var fixture = new RuntimeFixture();

        SchemataProcess?           addedProcess    = null;
        SchemataProcessTransition? addedTransition = null;
        fixture.Processes.Setup(r => r.AddAsync(It.IsAny<SchemataProcess>(), It.IsAny<CancellationToken>()))
               .Callback<SchemataProcess, CancellationToken>((p, _) => addedProcess = p)
               .Returns(SystemTask.CompletedTask);
        fixture.Transitions.Setup(r => r.AddAsync(It.IsAny<SchemataProcessTransition>(), It.IsAny<CancellationToken>()))
               .Callback<SchemataProcessTransition, CancellationToken>((t, _) => addedTransition = t)
               .Returns(SystemTask.CompletedTask);

        await fixture.Runtime.StartProcessInstanceAsync("approval");

        Assert.NotNull(addedProcess);
        Assert.NotNull(addedTransition);
        Assert.Equal(addedProcess!.Name, addedTransition!.Process);
    }

    [Fact]
    public async SystemTask FlowTransitionAdvisorFailure_AbortsBeforeCommit() {
        var advisor = new Mock<IFlowTransitionAdvisor>();
        advisor.SetupGet(a => a.Order).Returns(0);
        advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<FlowTransitionContext>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("provisioning failed"));

        var fixture = new RuntimeFixture(transitionAdvisor: advisor.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Runtime.StartProcessInstanceAsync("approval").AsTask());

        // Provisioning runs before the transition is persisted, so a failure leaves the unit of
        // work (and thus the commit) untouched rather than stranding a committed instance.
        Assert.Empty(fixture.UnitOfWorks);
    }

    [Fact]
    public async SystemTask StartProcessInstanceAsync_PersistenceFailurePropagatesAndLeavesNoCachedInstance() {
        var fixture = new RuntimeFixture();
        fixture.FailCommit = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runtime.StartProcessInstanceAsync("approval").AsTask());

        var canonicalName = fixture.StartedCanonicalName;
        Assert.NotNull(canonicalName);
        var uow = Assert.Single(fixture.UnitOfWorks);
        uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);

        fixture.FailCommit = false;
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Runtime.CompleteActivityAsync(canonicalName!).AsTask());
    }

    [Fact]
    public async SystemTask ThrowSignalAsync_HydratesPersistedWaitingProcessBeforeDelivery() {
        var fixture = new RuntimeFixture();
        fixture.Persisted.Add(new() {
            Name           = "persisted",
            CanonicalName  = "processes/persisted",
            DefinitionName = "approval",
            StateId        = "gateway",
            State          = "Gateway",
            WaitingAtId    = "gateway",
            WaitingAt      = "Gateway",
        });

        await fixture.Runtime.ThrowSignalAsync("paid");

        fixture.Engine.Verify(e => e.TriggerAsync(
                                  It.IsAny<ProcessDefinition>(),
                                  It.Is<SchemataProcess>(p => p.CanonicalName == "processes/persisted"),
                                  It.IsAny<IEventDefinition>(),
                                  It.IsAny<object?>(),
                                  It.IsAny<CancellationToken>()),
                              Times.Once);
        fixture.Processes.Verify(
            r => r.UpdateAsync(It.Is<SchemataProcess>(p => p.StateId == "done"), It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Transitions.Verify(
            r => r.AddAsync(It.Is<SchemataProcessTransition>(t => t.Event == "paid"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class RuntimeFixture
    {
        public Mock<IFlowRuntime> Engine { get; } = new();
        public bool FailCommit { get; set; }
        public List<SchemataProcess> Persisted { get; } = [];
        public Mock<IRepository<SchemataProcess>> Processes { get; } = new();
        public ProcessRuntime Runtime { get; }
        public string? StartedCanonicalName { get; private set; }
        public Mock<IRepository<SchemataProcessTransition>> Transitions { get; } = new();
        public List<Mock<IUnitOfWork>> UnitOfWorks { get; } = [];

        public RuntimeFixture(IFlowTransitionAdvisor? transitionAdvisor = null) {
            var definition = CreateSignalDefinition();
            var registration = new ProcessRegistration {
                Name          = definition.Name,
                Engine        = SchemataConstants.FlowEngines.StateMachine,
                Definition    = definition,
                Configuration = new() { Name = definition.Name },
            };

            var registry = new Mock<IProcessRegistry>();
            registry.Setup(r => r.IsRegistered(definition.Name)).Returns(true);
            registry.Setup(r => r.GetRegistration(definition.Name)).Returns(registration);
            registry.Setup(r => r.GetRegisteredProcesses()).Returns([definition.Name]);

            Engine.SetupGet(e => e.EngineName).Returns(SchemataConstants.FlowEngines.StateMachine);
            Engine.Setup(e => e.StartAsync(It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(), It.IsAny<CancellationToken>()))
                  .Callback((ProcessDefinition _, SchemataProcess process, CancellationToken _) => StartedCanonicalName = process.CanonicalName)
                  .ReturnsAsync(new ProcessInstance { StateId = "draft", State = "Draft" });
            Engine.Setup(e => e.TriggerAsync(
                             It.IsAny<ProcessDefinition>(),
                             It.IsAny<SchemataProcess>(),
                             It.IsAny<IEventDefinition>(),
                             It.IsAny<object?>(),
                             It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new ProcessInstance { StateId = "done", State = "Done" });
            Engine.Setup(e => e.AdvanceAsync(It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new ProcessInstance { StateId = "review", State = "Review" });

            Processes.Setup(r => r.FirstOrDefaultAsync(
                                It.IsAny<Func<IQueryable<SchemataProcess>, IQueryable<SchemataProcess>>>(),
                                It.IsAny<CancellationToken>()))
                     .Returns((Func<IQueryable<SchemataProcess>, IQueryable<SchemataProcess>> predicate, CancellationToken _)
                                  => ValueTask.FromResult(predicate(Persisted.AsQueryable()).FirstOrDefault()));
            Processes.Setup(r => r.ListAsync(
                                It.IsAny<Func<IQueryable<SchemataProcess>, IQueryable<SchemataProcess>>>(),
                                It.IsAny<CancellationToken>()))
                     .Returns((Func<IQueryable<SchemataProcess>, IQueryable<SchemataProcess>> predicate, CancellationToken _)
                                  => ToAsyncEnumerable(predicate(Persisted.AsQueryable()).ToList()));
            Processes.Setup(r => r.Begin()).Returns(() => CreateUnitOfWork().Object);

            var services = new ServiceCollection();
            services.AddSingleton(Options.Create(new SchemataFlowOptions()));
            services.AddSingleton(registry.Object);
            services.AddKeyedSingleton(SchemataConstants.FlowEngines.StateMachine, Engine.Object);
            services.AddSingleton(Processes.Object);
            services.AddSingleton(Transitions.Object);
            if (transitionAdvisor is not null) {
                services.AddSingleton(transitionAdvisor);
            }

            Runtime = new(registry.Object, services.BuildServiceProvider());
        }

        private Mock<IUnitOfWork> CreateUnitOfWork() {
            var uow = new Mock<IUnitOfWork>();
            uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
               .Returns(() => FailCommit
                            ? throw new InvalidOperationException("commit failed")
                            : SystemTask.CompletedTask);
            UnitOfWorks.Add(uow);
            return uow;
        }

        private static async IAsyncEnumerable<SchemataProcess> ToAsyncEnumerable(IEnumerable<SchemataProcess> items) {
            foreach (var item in items) {
                yield return item;
                await SystemTask.Yield();
            }
        }

        private static ProcessDefinition CreateSignalDefinition() {
            var signal = new Signal { Name = "paid" };
            var gateway = new EventBasedGateway { Id = "gateway", Name = "Gateway" };
            var catchEvent = new FlowEvent {
                Id         = "catch-paid",
                Name       = "CatchPaid",
                Position   = EventPosition.IntermediateCatch,
                Definition = signal,
            };
            var done = new NoneTask { Id = "done", Name = "Done" };

            return new() {
                Name = "approval",
                Elements = { gateway, catchEvent, done },
                Signals = { signal },
                Flows = {
                    new() { Id = "f1", Source = gateway, Target    = catchEvent },
                    new() { Id = "f2", Source = catchEvent, Target = done },
                },
            };
        }
    }
}
