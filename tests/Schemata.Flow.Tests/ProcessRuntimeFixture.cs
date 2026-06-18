using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Tests;

internal sealed class ProcessRuntimeFixture
{
    public Mock<IFlowRuntime> Engine { get; } = new();
    public RecordingEventBus EventBus { get; } = new();
    public ProcessInstance AdvanceResult { get; set; } = new() { StateId = "review", State = "Review" };
    public Exception? AdvanceException { get; set; }
    public List<SchemataProcess> Persisted { get; } = [];
    public Mock<IRepository<SchemataProcess>> Processes { get; } = new();
    public ProcessRuntime Runtime { get; }
    public ProcessInstance StartResult { get; set; } = new() { StateId = "draft", State = "Draft" };
    public Exception? StartException { get; set; }
    public Mock<IRepository<SchemataProcessTransition>> Transitions { get; } = new();
    public List<Mock<IUnitOfWork>> UnitOfWorks { get; } = [];

    public string? AdvancedStateId { get; private set; }

    public ProcessRuntimeFixture() {
        var definition = CreateDefinition();
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
              .Returns((ProcessDefinition _, SchemataProcess _, CancellationToken _) => {
                  if (StartException is not null) {
                      throw StartException;
                  }

                  return new(StartResult);
              });
        Engine.Setup(e => e.AdvanceAsync(It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(), It.IsAny<CancellationToken>()))
              .Callback((ProcessDefinition _, SchemataProcess process, CancellationToken _) => AdvancedStateId = process.StateId)
              .Returns((ProcessDefinition _, SchemataProcess _, CancellationToken _) => {
                  if (AdvanceException is not null) {
                      throw AdvanceException;
                  }

                  return new(AdvanceResult);
              });

        Processes.Setup(r => r.FirstOrDefaultAsync(
                            It.IsAny<Func<IQueryable<SchemataProcess>, IQueryable<SchemataProcess>>>(),
                            It.IsAny<CancellationToken>()))
                 .Returns((Func<IQueryable<SchemataProcess>, IQueryable<SchemataProcess>> predicate, CancellationToken _)
                              => ValueTask.FromResult(predicate(Persisted.AsQueryable()).FirstOrDefault()));
        Processes.Setup(r => r.ListAsync(
                            It.IsAny<Func<IQueryable<SchemataProcess>, IQueryable<SchemataProcess>>>(),
                            It.IsAny<CancellationToken>()))
                 .Returns((Func<IQueryable<SchemataProcess>, IQueryable<SchemataProcess>> predicate, CancellationToken _)
                              => ToAsync(predicate(Persisted.AsQueryable()).ToList()));
        Processes.Setup(r => r.AddAsync(It.IsAny<SchemataProcess>(), It.IsAny<CancellationToken>()))
                 .Callback((SchemataProcess process, CancellationToken _) => Persisted.Add(process))
                 .Returns(Task.CompletedTask);
        Processes.Setup(r => r.UpdateAsync(It.IsAny<SchemataProcess>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        Processes.Setup(r => r.Begin()).Returns(() => CreateUnitOfWork().Object);

        Transitions.Setup(r => r.AddAsync(It.IsAny<SchemataProcessTransition>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new SchemataFlowOptions()));
        services.AddSingleton(registry.Object);
        services.AddSingleton<IEventBus>(EventBus);
        services.AddKeyedSingleton(SchemataConstants.FlowEngines.StateMachine, Engine.Object);
        services.AddSingleton(Processes.Object);
        services.AddSingleton(Transitions.Object);

        Runtime = new(registry.Object, services.BuildServiceProvider());
    }

    public static void MutatePersisted(SchemataProcess process, string stateId, string state) {
        process.StateId = stateId;
        process.State   = state;
    }

    private Mock<IUnitOfWork> CreateUnitOfWork() {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        UnitOfWorks.Add(uow);
        return uow;
    }

    private static async IAsyncEnumerable<SchemataProcess> ToAsync(IEnumerable<SchemataProcess> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.Yield();
        }
    }

    private static ProcessDefinition CreateDefinition() {
        var start  = new StartEvent { Id = "start", Name = "Start" };
        var draft  = new NoneTask { Id = "draft", Name = "Draft" };
        var review = new NoneTask { Id = "review", Name = "Review" };
        var done   = new EndEvent { Id = "done", Name = "Done" };

        return new() {
            Name = "approval",
            Elements = { start, draft, review, done },
            Flows = {
                new() { Id = "f1", Source = start, Target  = draft },
                new() { Id = "f2", Source = draft, Target  = review },
                new() { Id = "f3", Source = review, Target = done },
            },
        };
    }

    internal sealed class RecordingEventBus : IEventBus
    {
        public List<(IEvent Event, object? Source)> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : IEvent {
            Published.Add((@event, null));
            return Task.CompletedTask;
        }

        public Task PublishAsync<TEvent>(TEvent @event, object sourceEntity, CancellationToken ct = default)
            where TEvent : IEvent {
            IEventBus.EnsureSourceEntityContract(sourceEntity);
            Published.Add((@event, sourceEntity));
            return Task.CompletedTask;
        }

        public Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest<TResponse> {
            throw new NotSupportedException();
        }
    }

    internal sealed class SourceEntity : ICanonicalName, IConcurrency
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        public Guid Timestamp { get; set; }
    }

    internal sealed class NamedOnlySource : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
}
