using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Xunit;
using CompleteActivityRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;
using StartProcessRequest = Schemata.Flow.Foundation.Commands.StartProcessRequest;

namespace Schemata.Flow.Tests;

/// <summary>
///     Proves the AIP-136 verb envelope reaches Flow's original command handlers through the
///     dispatcher and exposes (verb, name, entity) to wrap-position advisors on both entry styles:
///     a direct <see cref="IRequestDispatcher" /> envelope dispatch and the
///     <see cref="FlowRunner" /> facade.
/// </summary>
public sealed class FlowMethodEnvelopeShould
{
    [Fact]
    public async Task Start_Envelope_Dispatch_Runs_The_Start_Handler_And_Exposes_The_Verb_To_Wraps() {
        var wrap    = new RecordingStartEnvelopeAdvisor();
        var command = new RecordingStartCommandAdvisor();
        var harness = CreateHarness(services => {
            services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>>(wrap);
            services.AddSingleton<IRequestPipelineAdvisor<StartProcessRequest, SchemataProcess>>(command);
        });
        var dispatcher = harness.Services.GetRequiredService<IRequestDispatcher>();
        var principal  = new ClaimsPrincipal(new ClaimsIdentity());

        var process = await dispatcher.SendAsync<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(
            new(FlowOperations.Start,
                null,
                new("envelope-process", Source: null, SourceType: null, SourceCanonicalName: null, Options: null, principal),
                principal),
            CancellationToken.None);

        var observed = Assert.Single(wrap.Observed);
        Assert.Equal(FlowOperations.Start, observed.Verb);
        Assert.Null(observed.Name);
        Assert.Equal(typeof(SchemataProcess), observed.Entity);
        Assert.NotNull(process.CanonicalName);
        Assert.Equal(1, command.Count);
        Assert.Equal(1, harness.EngineStarts);
    }

    [Fact]
    public async Task Start_Facade_Wraps_The_Verb_Envelope() {
        var wrap    = new RecordingStartEnvelopeAdvisor();
        var command = new RecordingStartCommandAdvisor();
        var harness = CreateHarness(services => {
            services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>>(wrap);
            services.AddSingleton<IRequestPipelineAdvisor<StartProcessRequest, SchemataProcess>>(command);
        });
        var facade = harness.Services.GetRequiredService<FlowRunner>();

        await facade.StartAsync("envelope-process", null, null, CancellationToken.None);

        var observed = Assert.Single(wrap.Observed);
        Assert.Equal(FlowOperations.Start, observed.Verb);
        Assert.Null(observed.Name);
        Assert.Equal(typeof(SchemataProcess), observed.Entity);
        Assert.Equal(1, command.Count);
        Assert.Equal(1, harness.EngineStarts);
    }

    [Fact]
    public async Task Complete_Envelope_Dispatch_Exposes_The_Verb_And_The_Process_Name_To_Wraps() {
        var wrap    = new RecordingCompleteEnvelopeAdvisor();
        var command = new RecordingCompleteCommandAdvisor();
        var harness = CreateHarness(services => {
            services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataProcess, CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>>(wrap);
            services.AddSingleton<IRequestPipelineAdvisor<CompleteActivityRequest, ProcessSnapshot>>(command);
        });
        var dispatcher = harness.Services.GetRequiredService<IRequestDispatcher>();

        var snapshot = await dispatcher.SendAsync<ResourceMethodRequest<SchemataProcess, CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>(
            new(FlowOperations.Complete, "processes/p1", new("processes/p1", null, null), null),
            CancellationToken.None);

        var observed = Assert.Single(wrap.Observed);
        Assert.Equal(FlowOperations.Complete, observed.Verb);
        Assert.Equal("processes/p1", observed.Name);
        Assert.Equal(typeof(SchemataProcess), observed.Entity);
        Assert.Equal("processes/p1", snapshot.Process.CanonicalName);
        Assert.Equal(1, command.Count);
    }

    [Fact]
    public async Task Complete_Facade_Wraps_The_Verb_Envelope_With_The_Process_Name() {
        var wrap    = new RecordingCompleteEnvelopeAdvisor();
        var command = new RecordingCompleteCommandAdvisor();
        var harness = CreateHarness(services => {
            services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<SchemataProcess, CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>>(wrap);
            services.AddSingleton<IRequestPipelineAdvisor<CompleteActivityRequest, ProcessSnapshot>>(command);
        });
        var facade = harness.Services.GetRequiredService<FlowRunner>();

        await facade.CompleteAsync(
            new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "envelope-process" },
            null, null, CancellationToken.None);

        var observed = Assert.Single(wrap.Observed);
        Assert.Equal(FlowOperations.Complete, observed.Verb);
        Assert.Equal("processes/p1", observed.Name);
        Assert.Equal(typeof(SchemataProcess), observed.Entity);
        Assert.Equal(1, command.Count);
    }

    private static Harness CreateHarness(Action<IServiceCollection>? advisors = null) {
        var registration = new ProcessRegistration {
            Name          = "envelope-process",
            Engine        = FlowConstants.Engines.StateMachine,
            Definition    = new EnvelopeProcess(),
            Configuration = new ProcessConfiguration(),
        };

        var harness = new Harness();
        var engine  = new Mock<IFlowRuntime>();
        engine.Setup(e => e.StartAsync(
                  It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(), It.IsAny<FlowExecutionContext>(),
                  It.IsAny<CancellationToken>()))
              .Returns((ProcessDefinition _, SchemataProcess process, FlowExecutionContext _, CancellationToken _) => {
                  harness.EngineStarts++;
                  return new ValueTask<ProcessSnapshot>(new ProcessSnapshot { Process = process, Tokens = [], Transitions = [] });
              });
        engine.Setup(e => e.AdvanceAsync(
                  It.IsAny<ProcessDefinition>(), It.IsAny<SchemataProcess>(), It.IsAny<IReadOnlyList<SchemataProcessToken>>(),
                  It.IsAny<FlowExecutionContext>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .Returns((ProcessDefinition _, SchemataProcess process, IReadOnlyList<SchemataProcessToken> tokens,
                        FlowExecutionContext _, string? _, CancellationToken _) =>
                            new ValueTask<ProcessSnapshot>(new ProcessSnapshot { Process = process, Tokens = tokens, Transitions = [] }));

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegistration("envelope-process")).Returns(registration);

        var collection = new ServiceCollection()
                        .AddLogging()
                        .AddSingleton(registry.Object)
                        .AddSingleton<IOptions<SchemataFlowOptions>>(Options.Create(new SchemataFlowOptions()))
                        .AddSingleton(Repository(new SchemataProcess {
                            Name           = "p1",
                            CanonicalName  = "processes/p1",
                            DefinitionName = "envelope-process",
                        }).Object)
                        .AddSingleton(Repository<SchemataProcessToken>().Object)
                        .AddSingleton(Repository<SchemataProcessTransition>().Object)
                        .AddSingleton(Repository<SchemataProcessSource>().Object)
                        .AddSingleton(Repository<SchemataProcessCompensation>().Object)
                        .AddKeyedSingleton<IFlowRuntime>(FlowConstants.Engines.StateMachine, engine.Object);
        advisors?.Invoke(collection);
        collection.AddSchemataFlow();
        harness.Services = collection.BuildServiceProvider();
        return harness;
    }

    private static Mock<IRepository<T>> Repository<T>(params T[] items)
        where T : class {
        var data       = items.ToList();
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

    private sealed class RecordingStartEnvelopeAdvisor : IRequestPipelineAdvisor<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>
    {
        public List<(string Verb, string? Name, Type Entity)> Observed { get; } = [];

        public int Order => 0;

        public Task<SchemataProcess> AdviseAsync(
            AdviceContext                                                              ctx,
            ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess> request,
            RequestHandlerContinuation<SchemataProcess>                                 next,
            CancellationToken                                                          ct = default
        ) {
            Observed.Add((request.Verb, request.Name, request.GetType().GetGenericArguments()[0]));
            return next(ct);
        }
    }

    private sealed class RecordingCompleteEnvelopeAdvisor : IRequestPipelineAdvisor<ResourceMethodRequest<SchemataProcess, CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>
    {
        public List<(string Verb, string? Name, Type Entity)> Observed { get; } = [];

        public int Order => 0;

        public Task<ProcessSnapshot> AdviseAsync(
            AdviceContext                                                                      ctx,
            ResourceMethodRequest<SchemataProcess, CompleteActivityRequest, ProcessSnapshot>    request,
            RequestHandlerContinuation<ProcessSnapshot>                                         next,
            CancellationToken                                                                  ct = default
        ) {
            Observed.Add((request.Verb, request.Name, request.GetType().GetGenericArguments()[0]));
            return next(ct);
        }
    }

    private sealed class RecordingStartCommandAdvisor : IRequestPipelineAdvisor<StartProcessRequest, SchemataProcess>
    {
        public int Count { get; private set; }

        public int Order => 0;

        public Task<SchemataProcess> AdviseAsync(
            AdviceContext                                ctx,
            StartProcessRequest                          request,
            RequestHandlerContinuation<SchemataProcess> next,
            CancellationToken                            ct = default) {
            Count++;
            return next(ct);
        }
    }

    private sealed class RecordingCompleteCommandAdvisor : IRequestPipelineAdvisor<CompleteActivityRequest, ProcessSnapshot>
    {
        public int Count { get; private set; }

        public int Order => 0;

        public Task<ProcessSnapshot> AdviseAsync(
            AdviceContext                               ctx,
            CompleteActivityRequest                     request,
            RequestHandlerContinuation<ProcessSnapshot> next,
            CancellationToken                           ct = default) {
            Count++;
            return next(ct);
        }
    }

    private sealed class Harness
    {
        public ServiceProvider Services { get; set; } = null!;

        public int EngineStarts { get; set; }
    }

    private sealed class EnvelopeProcess : ProcessDefinition;
}
