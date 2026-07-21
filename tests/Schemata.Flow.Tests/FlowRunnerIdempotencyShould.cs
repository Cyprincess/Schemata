using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Schemata.Flow.Foundation;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowRunnerIdempotencyShould
{
    [Fact]
    public async Task Defer_Live_Key_Conflicts_To_The_Store() {
        var live = new SchemataProcess {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = "def",
            IdempotencyKey = "key-1",
            State          = "Running",
        };
        var engine = Engine();
        var runner = Runner(out _, engine, live);

        var process = await runner.StartAsync("def", new StartProcessOptions { IdempotencyKey = "key-1" });

        Assert.Equal("key-1", process.IdempotencyKey);
        engine.Verify(e => e.StartAsync(
                          It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(),
                          It.IsAny<FlowExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Allow_Restart_When_Key_Matches_Terminal_Process() {
        var done = new SchemataProcess {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = "def",
            IdempotencyKey = "key-1",
            State          = "Completed",
        };
        var engine = Engine();
        var runner = Runner(out var processes, engine, done);

        var process = await runner.StartAsync("def", new StartProcessOptions { IdempotencyKey = "key-1" });

        Assert.Equal("key-1", process.IdempotencyKey);
        Assert.NotEqual("processes/p1", process.CanonicalName);
        engine.Verify(e => e.StartAsync(
                          It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(),
                          It.IsAny<FlowExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        processes.Verify(r => r.AddAsync(
                             It.Is<SchemataProcess>(p => p.IdempotencyKey == "key-1" && p.CanonicalName != "processes/p1"),
                             It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Skip_Idempotency_Check_When_Key_Is_Null() {
        var live = new SchemataProcess {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = "def",
            IdempotencyKey = "key-1",
            State          = "Running",
        };
        var engine = Engine();
        var runner = Runner(out var processes, engine, live);

        var process = await runner.StartAsync("def");

        Assert.Null(process.IdempotencyKey);
        engine.Verify(e => e.StartAsync(
                          It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(),
                          It.IsAny<FlowExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        processes.Verify(r => r.AddAsync(
                             It.Is<SchemataProcess>(p => p.IdempotencyKey == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IFlowRuntime> Engine() {
        var engine = new Mock<IFlowRuntime>();
        engine.Setup(e => e.StartAsync(
                  It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(),
                  It.IsAny<FlowExecutionContext>(), It.IsAny<CancellationToken>()))
              .Returns((ProcessDefinition d, SchemataProcess p, FlowExecutionContext c, CancellationToken ct) =>
                  new ValueTask<ProcessSnapshot>(new ProcessSnapshot { Process = p, Tokens = [], Transitions = [] }));
        return engine;
    }

    private static FlowRunner Runner(
        out Mock<IRepository<SchemataProcess>> processes,
        Mock<IFlowRuntime>                    engine,
        params SchemataProcess[]              seeded
    ) {
        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegistration("def"))
                .Returns(new ProcessRegistration {
                     Name          = "def",
                     Engine        = SchemataConstants.FlowEngines.StateMachine,
                     Definition    = new IdempotentProcess(),
                     Configuration = new ProcessConfiguration(),
                 });

        processes = Repository(seeded);
        var tokens      = Repository<SchemataProcessToken>();
        var transitions = Repository<SchemataProcessTransition>();
        var sources     = Repository<SchemataProcessSource>();
        var compensations = Repository<SchemataProcessCompensation>();
        processes.Setup(r => r.Begin()).Returns(Mock.Of<IUnitOfWork>());

        var services = new ServiceCollection()
                      .AddSingleton(processes.Object)
                      .AddSingleton(tokens.Object)
                      .AddSingleton(transitions.Object)
                      .AddSingleton(sources.Object)
                      .AddSingleton(compensations.Object)
                      .AddKeyedSingleton<IFlowRuntime>(SchemataConstants.FlowEngines.StateMachine, engine.Object)
                      .BuildServiceProvider();

        var notifier = new ProcessLifecycleNotifier([], Mock.Of<ILogger<ProcessLifecycleNotifier>>());
        return new(registry.Object, new ProcessPersistence(), notifier, services);
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

    private sealed class IdempotentProcess : ProcessDefinition;
}
