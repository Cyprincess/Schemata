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
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowRunnerPayloadShould
{
    [Fact]
    public async Task Correlate_Binds_Payload_Case_Insensitively() {
        var definition = new PayloadProcess();
        definition.Messages.Add(new Message { Name = "Greet" });

        var registration = new ProcessRegistration {
            Name                = "greet-process",
            Engine              = SchemataConstants.FlowEngines.StateMachine,
            Definition          = definition,
            Configuration       = new ProcessConfiguration(),
            MessagePayloadTypes = new Dictionary<string, Type> { ["Greet"] = typeof(GreetPayload) },
        };

        object? captured = null;
        var engine = new Mock<IFlowRuntime>();
        engine.Setup(e => e.FindTriggerTargetsAsync(
                  It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(),
                  It.IsAny<IReadOnlyList<SchemataProcessToken>>(), It.IsAny<FlowExecutionContext>(),
                  It.IsAny<IEventDefinition>(), It.IsAny<CancellationToken>()))
              .Returns(new ValueTask<IReadOnlyList<string>>(new List<string> { "processes/p1/tokens/t1" }));
        engine.Setup(e => e.TriggerAsync(
                  It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(),
                  It.IsAny<IReadOnlyList<SchemataProcessToken>>(), It.IsAny<FlowExecutionContext>(),
                  It.IsAny<IEventDefinition>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .Returns((ProcessDefinition d, SchemataProcess p, IReadOnlyList<SchemataProcessToken> t, FlowExecutionContext c,
                        IEventDefinition e, object? payload, string? token, CancellationToken ct) => {
                  captured = payload;
                  return new ValueTask<ProcessSnapshot>(new ProcessSnapshot { Process = p, Tokens = [], Transitions = [] });
              });

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegistration("greet-process")).Returns(registration);

        var processes = Repository<SchemataProcess>();
        var tokens    = Repository<SchemataProcessToken>();
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
        var runner   = new FlowRunner(registry.Object, new ProcessPersistence(), notifier, services);
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "greet-process" };

        await runner.CorrelateAsync(process, "Greet", """{"gReEtInG":"hello","cOuNt":3}""", null, null, CancellationToken.None);

        var value = Assert.IsType<GreetPayload>(captured);
        Assert.Equal("hello", value.Greeting);
        Assert.Equal(3, value.Count);
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

    private sealed class PayloadProcess : ProcessDefinition;

    public sealed class GreetPayload
    {
        public string? Greeting { get; set; }

        public int Count { get; set; }
    }
}
