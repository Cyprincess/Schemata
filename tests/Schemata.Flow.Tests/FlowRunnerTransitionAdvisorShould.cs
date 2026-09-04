using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowRunnerTransitionAdvisorShould
{
    [Fact]
    public async Task Advise_On_The_Transition_Carrying_Its_Token_And_Unit_Of_Work() {
        var harness = CreateHarness([Advisor("only", AdviseResult.Continue)]);

        await harness.Runner.StartAsync("advisor-process", null, null, CancellationToken.None);

        var context = Assert.Single(harness.Advised);
        Assert.Equal("processes/p1/tokens/t1", context.Token.CanonicalName);
        Assert.NotNull(context.UnitOfWork);
    }

    [Fact]
    public async Task Advise_In_Ascending_Order_Regardless_Of_Registration_Order() {
        var harness = CreateHarness([
            Advisor("late", AdviseResult.Continue, 20),
            Advisor("early", AdviseResult.Continue, 10),
        ]);

        await harness.Runner.StartAsync("advisor-process", null, null, CancellationToken.None);

        Assert.Equal(["early", "late"], harness.Calls);
    }

    [Fact]
    public async Task Stop_Later_Advisors_When_One_Blocks() {
        var harness = CreateHarness([
            Advisor("blocking", AdviseResult.Block, 10),
            Advisor("later", AdviseResult.Continue, 20),
        ]);

        await harness.Runner.StartAsync("advisor-process", null, null, CancellationToken.None);

        Assert.Equal(["blocking"], harness.Calls);
    }

    [Fact]
    public async Task Arm_Catch_Handlers_Even_When_An_Advisor_Blocks() {
        var harness = CreateHarness([Advisor("blocking", AdviseResult.Block)]);

        await harness.Runner.StartAsync("advisor-process", null, null, CancellationToken.None);

        Assert.Equal("processes/p1/tokens/t1", Assert.Single(harness.Armed).Token.CanonicalName);
    }

    [Fact]
    public async Task Abort_Before_Persistence_When_An_Advisor_Throws() {
        var harness = CreateHarness([ThrowingAdvisor("rejecting")]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await harness.Runner.StartAsync("advisor-process", null, null, CancellationToken.None));

        Assert.Equal("rejecting", exception.Message);
        Assert.Empty(harness.Armed);
        harness.UnitOfWork.Verify(uow => uow.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Func<Harness, Mock<IFlowTransitionAdvisor>> Advisor(
        string       name,
        AdviseResult result,
        int          order = 0
    ) {
        return harness => {
            var advisor = new Mock<IFlowTransitionAdvisor>();
            advisor.SetupGet(a => a.Order).Returns(order);
            advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<FlowTransitionContext>(),
                                             It.IsAny<CancellationToken>()))
                   .Returns((AdviceContext _, FlowTransitionContext context, CancellationToken _) => {
                       harness.Calls.Add(name);
                       harness.Advised.Add(context);
                       return Task.FromResult(result);
                   });
            return advisor;
        };
    }

    private static Func<Harness, Mock<IFlowTransitionAdvisor>> ThrowingAdvisor(string name, int order = 0) {
        return harness => {
            var advisor = new Mock<IFlowTransitionAdvisor>();
            advisor.SetupGet(a => a.Order).Returns(order);
            advisor.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<FlowTransitionContext>(),
                                             It.IsAny<CancellationToken>()))
                   .Returns((AdviceContext _, FlowTransitionContext _, CancellationToken _) => {
                       harness.Calls.Add(name);
                       throw new InvalidOperationException(name);
                   });
            return advisor;
        };
    }

    private static Harness CreateHarness(IReadOnlyList<Func<Harness, Mock<IFlowTransitionAdvisor>>> advisors) {
        var registration = new ProcessRegistration {
            Name          = "advisor-process",
            Engine        = FlowConstants.Engines.StateMachine,
            Definition    = new AdvisorProcess(),
            Configuration = new(),
        };

        var harness = new Harness();

        var engine = new Mock<IFlowRuntime>();
        engine.Setup(e => e.StartAsync(
                  It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(),
                  It.IsAny<FlowExecutionContext>(), It.IsAny<CancellationToken>()))
              .Returns((ProcessDefinition _, SchemataProcess p, FlowExecutionContext _, CancellationToken _) =>
                           new(Snapshot(p)));

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegistration("advisor-process")).Returns(registration);

        var handler = new Mock<IFlowCatchHandler>();
        handler.Setup(h => h.ArmAsync(It.IsAny<FlowTransitionContext>(), It.IsAny<CancellationToken>()))
               .Returns((FlowTransitionContext context, CancellationToken _) => {
                   harness.Armed.Add(context);
                   return ValueTask.CompletedTask;
               });

        var processes = Repository(harness.UnitOfWork.Object, new SchemataProcess {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = "advisor-process",
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
                        .AddSingleton(handler.Object)
                        .AddKeyedSingleton<IFlowRuntime>(FlowConstants.Engines.StateMachine, engine.Object);

        foreach (var advisor in advisors) {
            collection.AddSingleton(advisor(harness).Object);
        }

        collection.AddSchemataFlow();
        var services = collection.BuildServiceProvider();

        harness.Runner = services.GetRequiredService<FlowRunner>();
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

        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public List<string> Calls { get; } = [];

        public List<FlowTransitionContext> Advised { get; } = [];

        public List<FlowTransitionContext> Armed { get; } = [];
    }

    private sealed class AdvisorProcess : ProcessDefinition;
}
