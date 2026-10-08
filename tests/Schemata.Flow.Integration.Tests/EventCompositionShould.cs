using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Event.Foundation.Runtime;
using Schemata.Actor.Event;
using Schemata.Actor.Skeleton;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Event.Features;
using Schemata.Flow.Event.Handlers;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Xunit;
using ThrowSignalRequest = Schemata.Flow.Foundation.Commands.ThrowSignalRequest;

namespace Schemata.Flow.Integration.Tests;

/// <summary>
///     One published event matches three subscriber kinds at once: a typed
///     <see cref="IEventHandler{TEvent}" />, the Flow catch-all bridge with a real armed signal
///     subscription, and an Actor event forwarder. Asserts each candidate receives exactly one
///     delivery through the real in-process bus and that the signal actually advances the waiting
///     process.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EventCompositionShould : IAsyncLifetime
{
    private const string SignalName = "broadcast-signal";

    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";

    private ServiceProvider _root = null!;

    private CountingSignalAdvisor SignalThrows { get; } = new();

    private RecordingSignalHandler TypedHandler { get; } = new();

    public async Task InitializeAsync() {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EventCompositionDbContext>(options => options.UseSqlite($"Data Source={_dbPath}")
                                                                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataProcess, EfCoreRepository<EventCompositionDbContext, SchemataProcess>>();
        services.AddRepository<SchemataProcessToken, EfCoreRepository<EventCompositionDbContext, SchemataProcessToken>>();
        services.AddRepository<SchemataProcessTransition, EfCoreRepository<EventCompositionDbContext, SchemataProcessTransition>>();
        services.AddRepository<SchemataProcessSource, EfCoreRepository<EventCompositionDbContext, SchemataProcessSource>>();
        services.AddRepository<SchemataProcessCompensation, EfCoreRepository<EventCompositionDbContext, SchemataProcessCompensation>>();
        services.AddRepository<SchemataProcessParticipant, EfCoreRepository<EventCompositionDbContext, SchemataProcessParticipant>>();
        services.AddRepository<SchemataEventSubscription, EfCoreRepository<EventCompositionDbContext, SchemataEventSubscription>>();
        services.AddScoped<IUnitOfWork<EventCompositionDbContext>, EfCoreUnitOfWork<EventCompositionDbContext>>();
        services.AddOptions<SchemataFlowOptions>();
        FlowFixtureServices.AddResourceTypeResolver(services, typeof(SchemataProcess), typeof(SchemataProcessToken));
        FlowFixtureServices.AddFlowServices(services);

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseEvent()
               .RegisterEvent<ReviewSignal>(SignalName)
               .UseProducer(producer => producer.UseInProcess())
               .UseConsumer(consumer => consumer.UseInProcess());
        builder.UseActor(actor => {
            actor.Register<CompositionRecorderActor>("recorder");
            actor.UseEvent().RouteEvent<ReviewSignal, ReviewSignalRoute>();
        });
        builder.Invoke(services);

        // The audit observer would persist SchemataEvent rows; this composition asserts handler
        // delivery, not audit persistence.
        services.RemoveAll<IEventLifecycleObserver>();
        new SchemataFlowEventFeature().ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);

        services.AddSingleton<IEventHandler<ReviewSignal>>(TypedHandler);
        services.AddSingleton<IRequestPipelineAdvisor<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>>(SignalThrows);

        _root = services.BuildServiceProvider();

        await using var scope = _root.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EventCompositionDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() {
        await using (var scope = _root.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<EventCompositionDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await _root.DisposeAsync();

        if (System.IO.File.Exists(_dbPath)) {
            System.IO.File.Delete(_dbPath);
        }
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Same_Scoped_Runner_Rearms_Real_Catch_Subscriptions_Through_Completed_Units() {
        await using var scope = _root.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<RepeatedSignalProcess>(FlowConstants.Engines.Bpmn);
        var runner = scope.ServiceProvider.GetRequiredService<IFlowRunner>();
        var process = await runner.StartAsync(nameof(RepeatedSignalProcess));
        var delivery = scope.ServiceProvider.GetRequiredService<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>>();
        var first = await delivery.HandleAsync(new(process.CanonicalName!, "repeat", null, null, null), CancellationToken.None);
        Assert.Equal(SignalDeliveryStatus.Delivered, first.Status);
        var second = await delivery.HandleAsync(new(process.CanonicalName!, "repeat", null, null, null), CancellationToken.None);
        Assert.Equal(SignalDeliveryStatus.Delivered, second.Status);
        await using var reader = _root.CreateAsyncScope();
        var token = await reader.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>()
            .SingleOrDefaultAsync(q => q.Where(row => row.Process == process.Name));
        Assert.NotNull(token);
        Assert.Equal("Completed", token.State);
        Assert.Equal("end", token.StateName);
        var subscriptions = await reader.ServiceProvider.GetRequiredService<IRepository<SchemataEventSubscription>>()
            .ListAsync(q => q.Where(row => row.Target == process.CanonicalName)).ToListAsync();
        Assert.Contains(subscriptions, row => row.SubscriptionId == $"flow:{process.CanonicalName}:second:broadcast"
            && row.EventType == "repeat" && row.Token is null);
    }

    public sealed class RepeatedSignalProcess : ProcessDefinition
    {
        public RepeatedSignalProcess() {
            var signal = new Signal { Name = "repeat" };
            var start = new StartEvent { Name = "start" };
            var first = new FlowEvent { Name = "first", Position = EventPosition.IntermediateCatch, Definition = signal };
            var second = new FlowEvent { Name = "second", Position = EventPosition.IntermediateCatch, Definition = signal };
            var end = new EndEvent { Name = "end" };
            Elements.AddRange([start, first, second, end]); Signals.Add(signal);
            Flows.Add(new() { Source = start, Target = first });
            Flows.Add(new() { Source = first, Target = second });
            Flows.Add(new() { Source = second, Target = end });
        }
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Publish_One_Event_Delivers_Once_To_Typed_Handler_Flow_Signal_And_Actor_Forwarder() {
        var definition = $"{nameof(SignalBroadcastProcess)}-{Guid.NewGuid():n}";
        await using (var scope = _root.CreateAsyncScope()) {
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            await registry.RegisterAsync(new() {
                Name           = definition,
                Engine         = FlowConstants.Engines.Bpmn,
                DefinitionType = typeof(SignalBroadcastProcess),
            });
        }

        string processName;
        await using (var scope = _root.CreateAsyncScope()) {
            var runner  = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            var process = await runner.StartAsync(definition, null, CancellationToken.None);
            processName = process.Name!;
        }

        var @event = new ReviewSignal(processName);
        await using (var scope = _root.CreateAsyncScope()) {
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            await bus.PublishAsync(@event);
        }

        Assert.Equal(1, TypedHandler.Calls);
        Assert.Equal(1, SignalThrows.Calls);

        // The flow bridge swallows per-target delivery failures into the result list, so assert the
        // recorded outcome instead of inferring delivery from the token state alone.
        var delivery = Assert.Single(SignalThrows.Results ?? []);
        Assert.Equal(SignalDeliveryStatus.Delivered, delivery.Status);

        await using (var scope = _root.CreateAsyncScope()) {
            var tokens = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>();
            var token  = await tokens.FirstOrDefaultAsync(q => q.Where(t => t.Process == processName));
            Assert.NotNull(token);
            Assert.Null(token.WaitingAtName);
        }

        var system = _root.GetRequiredService<IActorSystem>();
        var actor  = await system.GetAsync(new("recorder", processName));

        IMessage? received = null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (received is null && DateTime.UtcNow < deadline) {
            received = await actor.AskAsync<GetReceived, IMessage?>(new());
            if (received is null) {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }

        Assert.Equal(@event, received);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Propagate_Signal_Delivery_Failure_Instead_Of_Faking_Publish_Success() {
        // A signal broadcast reports per-target outcomes; the bridge rethrows the first fault so a
        // failed delivery can never read as a successful publish.
        var services = new ServiceCollection();
        services.AddInProcessRequestDispatcher();
        services.AddSingleton<IRequestHandler<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>>(
            new FailingSignalDeliveryHandler());
        await using var root = services.BuildServiceProvider();

        var dispatch = new EventDispatchContext();
        dispatch.SetSubscriptions([
            new() { Target = "processes/one", EventType = "broadcast-signal" },
        ]);

        var handler = new FlowEventHandler(root, dispatch);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(new ReviewSignal("key"), CancellationToken.None));
        Assert.Equal("delivery failed", ex.Message);
    }

    private sealed class FailingSignalDeliveryHandler : IRequestHandler<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>
    {
        public Task<IReadOnlyList<SignalDeliveryResult>> HandleAsync(ThrowSignalRequest request, CancellationToken ct) {
            IReadOnlyList<SignalDeliveryResult> results =
                [new("processes/one", SignalDeliveryStatus.Failed, new InvalidOperationException("delivery failed"))];
            return Task.FromResult(results);
        }
    }

    public sealed record ReviewSignal(string ProcessKey) : IEvent;

    public sealed record GetReceived : IRequest<IMessage?>;

    public sealed class ReviewSignalRoute : IEventActorRoute<ReviewSignal>
    {
        public ActorId? Resolve(ReviewSignal @event) => new("recorder", @event.ProcessKey);
    }

    public sealed class CompositionRecorderActor : IActor
    {
        private IMessage? _received;

        #region IActor Members

        public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

        public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
            switch (envelope.Payload) {
                case GetReceived:
                    await ctx.ReplyAsync<IMessage?>(_received);
                    break;
                default:
                    _received = envelope.Payload;
                    break;
            }
        }

        public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

        public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);

        #endregion
    }

    public sealed class RecordingSignalHandler : IEventHandler<ReviewSignal>
    {
        public int Calls;

        public Task HandleAsync(ReviewSignal @event, CancellationToken ct = default) {
            Calls++;
            return Task.CompletedTask;
        }
    }

    public sealed class CountingSignalAdvisor : IRequestPipelineAdvisor<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>
    {
        public int Order => 0;

        public int Calls;

        public IReadOnlyList<SignalDeliveryResult>? Results;

        public async Task<IReadOnlyList<SignalDeliveryResult>> AdviseAsync(
            AdviceContext                                        ctx,
            ThrowSignalRequest                                   request,
            RequestHandlerContinuation<IReadOnlyList<SignalDeliveryResult>> next,
            CancellationToken                                    ct
        ) {
            Calls++;
            Results = await next(ct);
            return Results;
        }
    }

    public sealed class EventCompositionDbContext(DbContextOptions<EventCompositionDbContext> options) : DbContext(options)
    {
        public DbSet<SchemataProcess>             Processes         { get; set; } = null!;
        public DbSet<SchemataProcessToken>        Tokens            { get; set; } = null!;
        public DbSet<SchemataProcessTransition>   Transitions       { get; set; } = null!;
        public DbSet<SchemataProcessSource>       Sources           { get; set; } = null!;
        public DbSet<SchemataProcessCompensation> Compensations     { get; set; } = null!;
        public DbSet<SchemataProcessParticipant>  Participants      { get; set; } = null!;
        public DbSet<SchemataEventSubscription>   EventSubscriptions { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<SchemataProcess>().Ignore(process => process.DisplayNames);
            modelBuilder.Entity<SchemataProcess>().Ignore(process => process.Descriptions);
            modelBuilder.Entity<SchemataProcessToken>().Ignore(token => token.Annotations);
            modelBuilder.Entity<SchemataProcessToken>().Ignore(token => token.Bookkeeping);
        }
    }
}
