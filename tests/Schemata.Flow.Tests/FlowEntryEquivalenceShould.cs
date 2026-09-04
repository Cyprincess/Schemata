using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using CompleteActivityRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;
using Xunit;

namespace Schemata.Flow.Tests;

/// <summary>
///     Proves the facade (<see cref="FlowRunner.CompleteAsync" />) and the unified
///     <see cref="IRequestDispatcher" /> entry run the exact same <see cref="CompleteActivityRequest" />
///     pipeline: equal <see cref="ProcessSnapshot" /> results, the registered
///     <see cref="IRequestPipelineAdvisor{TRequest,TResponse}" /> firing on both entries, and identical exception shapes
///     when the addressed process does not exist. Neither entry stubs the real
///     <c>DefaultCompleteActivityHandler</c> — only the state-machine engine and repositories are test
///     doubles, matching this project's existing <c>FlowRunnerTransitionAdvisorShould</c> fixture shape.
/// </summary>
public sealed class FlowEntryEquivalenceShould
{
    [Fact]
    public async Task Complete_Through_Facade_And_Dispatcher_Produce_Equivalent_Snapshots_And_Fire_The_Same_Advisor() {
        var facadeSpy     = new RecordingCommandAdvisor();
        var facadeHarness = CreateHarness(facadeSpy);
        var facadeSnapshot = await facadeHarness.Runner.CompleteAsync(
            facadeHarness.Process, null, null, CancellationToken.None);

        var dispatcherSpy     = new RecordingCommandAdvisor();
        var dispatcherHarness = CreateHarness(dispatcherSpy);
        var dispatcher         = dispatcherHarness.Services.GetRequiredService<IRequestDispatcher>();
        var dispatcherSnapshot = await dispatcher.SendAsync<CompleteActivityRequest, ProcessSnapshot>(
            new(dispatcherHarness.Process.CanonicalName!, null, null), CancellationToken.None);

        Assert.Equal(facadeSnapshot.Process.CanonicalName, dispatcherSnapshot.Process.CanonicalName);
        Assert.Equal(facadeSnapshot.Process.DefinitionName, dispatcherSnapshot.Process.DefinitionName);
        Assert.Equal(
            facadeSnapshot.Tokens.Select(token => (token.CanonicalName, token.Process, token.State)),
            dispatcherSnapshot.Tokens.Select(token => (token.CanonicalName, token.Process, token.State)));
        Assert.Equal(
            facadeSnapshot.Transitions.Select(transition => (transition.CanonicalName, transition.Token)),
            dispatcherSnapshot.Transitions.Select(transition => (transition.CanonicalName, transition.Token)));
        Assert.Equal(1, facadeSpy.Count);
        Assert.Equal(1, dispatcherSpy.Count);
    }

    [Fact]
    public async Task Complete_Throw_The_Same_Exception_Type_Through_Both_Entries_For_A_Missing_Process() {
        var missingProcess = new SchemataProcess {
            Name           = "missing",
            CanonicalName  = "processes/missing",
            DefinitionName = "equivalence-process",
        };

        var facadeHarness = CreateHarness(null);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            facadeHarness.Runner.CompleteAsync(missingProcess, null, null, CancellationToken.None).AsTask());

        var dispatcherHarness = CreateHarness(null);
        var dispatcher         = dispatcherHarness.Services.GetRequiredService<IRequestDispatcher>();
        await Assert.ThrowsAsync<NotFoundException>(() => dispatcher.SendAsync<CompleteActivityRequest, ProcessSnapshot>(
            new("processes/missing", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Dispatch_Continues_The_Ambient_Context_Into_Transition_Advisors() {
        var marker     = new MarkerCommandAdvisor();
        var observed   = (AdviceContext?)null;
        var transition = new Mock<IFlowTransitionAdvisor>();
        transition.Setup(a => a.AdviseAsync(
                      It.IsAny<AdviceContext>(), It.IsAny<FlowTransitionContext>(), It.IsAny<CancellationToken>()))
                  .Returns((AdviceContext ctx, FlowTransitionContext _, CancellationToken _) => {
                      observed = ctx;
                      return Task.FromResult(AdviseResult.Continue);
                  });
        var harness    = CreateHarness(marker, transition.Object);
        var dispatcher = harness.Services.GetRequiredService<IRequestDispatcher>();

        await dispatcher.SendAsync<CompleteActivityRequest, ProcessSnapshot>(
            new(harness.Process.CanonicalName!, null, null), CancellationToken.None);

        Assert.NotNull(observed);
        Assert.True(observed.TryGet<Marker>(out var value));
        Assert.Same(marker.Value, value);
    }

    private static Harness CreateHarness(
        IRequestPipelineAdvisor<CompleteActivityRequest, ProcessSnapshot>? advisor,
        IFlowTransitionAdvisor?                                            transitionAdvisor = null
    ) {
        var registration = new ProcessRegistration {
            Name          = "equivalence-process",
            Engine        = FlowConstants.Engines.StateMachine,
            Definition    = new EquivalenceProcess(),
            Configuration = new(),
        };

        var harness = new Harness();

        var engine = new Mock<IFlowRuntime>();
        engine.Setup(e => e.AdvanceAsync(
                  It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(), It.IsAny<IReadOnlyList<SchemataProcessToken>>(),
                  It.IsAny<FlowExecutionContext>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .Returns((ProcessDefinition _, SchemataProcess process, IReadOnlyList<SchemataProcessToken> _,
                        FlowExecutionContext _, string? _, CancellationToken _) =>
                            new(Snapshot(process)));

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegistration("equivalence-process")).Returns(registration);

        var processes = Repository(harness.UnitOfWork.Object, new SchemataProcess {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = "equivalence-process",
        });

        var collection = new ServiceCollection()
                        .AddLogging()
                        .AddSingleton(registry.Object)
                        .AddSingleton<IOptions<SchemataFlowOptions>>(Options.Create(new SchemataFlowOptions()))
                        .AddSingleton(processes.Object)
                        .AddSingleton(Repository<SchemataProcessToken>(harness.UnitOfWork.Object).Object)
                        .AddSingleton(Repository<SchemataProcessTransition>(harness.UnitOfWork.Object).Object)
                        .AddSingleton(Repository<SchemataProcessSource>(harness.UnitOfWork.Object).Object)
                        .AddSingleton(Repository<SchemataProcessCompensation>(harness.UnitOfWork.Object).Object)
                        .AddKeyedSingleton<IFlowRuntime>(FlowConstants.Engines.StateMachine, engine.Object);

        if (advisor is not null) {
            collection.AddSingleton(advisor);
        }
        if (transitionAdvisor is not null) {
            collection.AddSingleton(transitionAdvisor);
        }

        collection.AddSchemataFlow();
        var services = collection.BuildServiceProvider();

        harness.Services = services;
        harness.Runner   = services.GetRequiredService<FlowRunner>();
        harness.Process = new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = "equivalence-process",
        };
        return harness;
    }

    private static ProcessSnapshot Snapshot(SchemataProcess process) {
        var token = new SchemataProcessToken {
            Name          = "t1",
            CanonicalName = "processes/p1/tokens/t1",
            Process       = "p1",
            State         = "Completed",
        };
        var transition = new SchemataProcessTransition {
            Name          = "tr1",
            CanonicalName = "processes/p1/transitions/tr1",
            Token         = token.CanonicalName,
        };
        return new() { Process = process, Tokens = [token], Transitions = [transition] };
    }

    private static Mock<IRepository<T>> Repository<T>(IUnitOfWork unitOfWork, params T[] items)
        where T : class {
        var data       = items.ToList();
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.Begin()).Returns(unitOfWork);
        repository.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
                  .Returns((T entity, CancellationToken _) => {
                      data.Add(entity);
                      return Task.CompletedTask;
                  });
        repository.Setup(r => r.UpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.ListAsync<T>(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => Async(predicate(data.AsQueryable()).ToList()));
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => new(predicate(data.AsQueryable()).SingleOrDefault()));
        repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => new(predicate(data.AsQueryable()).FirstOrDefault()));
        return repository;
    }

    private static async IAsyncEnumerable<T> Async<T>(IEnumerable<T> items) {
        foreach (var item in items) {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FlowRunner Runner { get; set; } = null!;

        public ServiceProvider Services { get; set; } = null!;

        public SchemataProcess Process { get; set; } = null!;

        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
    }

    /// <summary>Records every dispatch of <see cref="CompleteActivityRequest" /> it observes.</summary>
    private sealed class RecordingCommandAdvisor : IRequestPipelineAdvisor<CompleteActivityRequest, ProcessSnapshot>
    {
        public int Count { get; private set; }

        public int Order => 0;

        public Task<ProcessSnapshot> AdviseAsync(
            AdviceContext                               ctx,
            CompleteActivityRequest                     a1,
            RequestHandlerContinuation<ProcessSnapshot> next,
            CancellationToken                           ct = default) {
            Count++;
            return next(ct);
        }
    }

    /// <summary>Stamps a <see cref="Marker" /> onto the dispatch's ambient context.</summary>
    private sealed class MarkerCommandAdvisor : IRequestPipelineAdvisor<CompleteActivityRequest, ProcessSnapshot>
    {
        public Marker Value { get; } = new();

        public int Order => 0;

        public Task<ProcessSnapshot> AdviseAsync(
            AdviceContext                               ctx,
            CompleteActivityRequest                     request,
            RequestHandlerContinuation<ProcessSnapshot> next,
            CancellationToken                           ct = default) {
            ctx.Set(Value);
            return next(ct);
        }
    }

    private sealed record Marker;

    private sealed class EquivalenceProcess : ProcessDefinition;
}
