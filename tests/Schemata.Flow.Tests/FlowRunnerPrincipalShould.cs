using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowRunnerPrincipalShould
{
    [Fact]
    public async Task Start_Exposes_Principal_To_Execution_Context_And_Transition_Advisor() {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var harness   = CreateHarness();

        await harness.Runner.StartAsync("principal-process", null, principal, CancellationToken.None);

        Assert.Same(principal, harness.CapturedExecution!.Principal);
        Assert.Same(principal, harness.CapturedContext!.Principal);
    }

    [Fact]
    public async Task RunEvent_Continuation_Carries_Null_Principal() {
        var harness = CreateHarness();

        await harness.Runner.RunEventAsync(
            "processes/p1",
            "processes/p1/tokens/t1",
            new TimerDefinition(),
            null,
            CancellationToken.None);

        Assert.Null(harness.CapturedExecution!.Principal);
        Assert.Null(harness.CapturedContext!.Principal);
    }

    private static Harness CreateHarness() {
        var definition = new PrincipalProcess();
        var registration = new ProcessRegistration {
            Name          = "principal-process",
            Engine        = SchemataConstants.FlowEngines.StateMachine,
            Definition    = definition,
            Configuration = new ProcessConfiguration(),
        };

        var harness = new Harness();

        var engine = new Mock<IFlowRuntime>();
        engine.Setup(e => e.StartAsync(
                  It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(),
                  It.IsAny<FlowExecutionContext>(), It.IsAny<CancellationToken>()))
              .Returns((ProcessDefinition _, SchemataProcess p, FlowExecutionContext ctx, CancellationToken _) => {
                  harness.CapturedExecution = ctx;
                  return new ValueTask<ProcessSnapshot>(Snapshot(p));
              });
        engine.Setup(e => e.TriggerAsync(
                  It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(),
                  It.IsAny<IReadOnlyList<SchemataProcessToken>>(), It.IsAny<FlowExecutionContext>(),
                  It.IsAny<IEventDefinition>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .Returns((ProcessDefinition _, SchemataProcess p, IReadOnlyList<SchemataProcessToken> _,
                        FlowExecutionContext ctx, IEventDefinition _, object? _, string? _, CancellationToken _) => {
                  harness.CapturedExecution = ctx;
                  return new ValueTask<ProcessSnapshot>(Snapshot(p));
              });

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegistration("principal-process")).Returns(registration);

        var advisor = new Mock<IFlowTransitionAdvisor>();
        advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<FlowTransitionContext>(), It.IsAny<CancellationToken>()))
               .Returns((AdviceContext _, FlowTransitionContext context, CancellationToken _) => {
                   harness.CapturedContext = context;
                   return Task.FromResult(AdviseResult.Continue);
               });

        var existing = new SchemataProcess {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = "principal-process",
        };

        var processes     = Repository(existing);
        var tokens        = Repository<SchemataProcessToken>();
        var transitions   = Repository<SchemataProcessTransition>();
        var sources       = Repository<SchemataProcessSource>();
        var compensations = Repository<SchemataProcessCompensation>();
        processes.Setup(r => r.Begin()).Returns(Mock.Of<IUnitOfWork>());

        var services = new ServiceCollection()
                      .AddSingleton(processes.Object)
                      .AddSingleton(tokens.Object)
                      .AddSingleton(transitions.Object)
                      .AddSingleton(sources.Object)
                      .AddSingleton(compensations.Object)
                      .AddSingleton(advisor.Object)
                      .AddKeyedSingleton<IFlowRuntime>(SchemataConstants.FlowEngines.StateMachine, engine.Object)
                      .BuildServiceProvider();

        var notifier = new ProcessLifecycleNotifier([], Mock.Of<ILogger<ProcessLifecycleNotifier>>());
        harness.Runner = new FlowRunner(registry.Object, new ProcessPersistence(), notifier, services);
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

    private static Mock<IRepository<T>> Repository<T>(params T[] items)
        where T : class {
        var data = items.ToList();
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.Begin()).Returns(Mock.Of<IUnitOfWork>());
        repository.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
                  .Returns((T entity, CancellationToken _) => {
                      data.Add(entity);
                      return Task.CompletedTask;
                  });
        repository.Setup(r => r.UpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.ListAsync<T>(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => Async(predicate(data.AsQueryable()).ToList()));
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => new ValueTask<T?>(predicate(data.AsQueryable()).SingleOrDefault()));
        repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => new ValueTask<T?>(predicate(data.AsQueryable()).FirstOrDefault()));
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

        public FlowExecutionContext? CapturedExecution { get; set; }

        public FlowTransitionContext? CapturedContext { get; set; }
    }

    private sealed class PrincipalProcess : ProcessDefinition;
}
