using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Actor.Event.Tests.Fixtures;
using Schemata.Actor.Foundation;
using Schemata.Actor.Skeleton;
using Schemata.Core;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Actor.Event.Tests;

/// <summary>
///     Exercises the event-to-mailbox bridge through the real <see cref="IEventBus" /> pipeline - the
///     same producer/observer/outbox/consumer path a production host wires - so a mistake in how
///     <c>RouteEvent</c> registers the forwarder against that pipeline (wrong service type, wrong
///     lifetime, the bus never resolving it) surfaces as a failed publish or an empty mailbox rather
///     than being masked by invoking the handler directly. <see cref="IRepository{TEntity}" /> for the
///     bus's own outbox/subscription audit rows is a functional in-memory double (the same style
///     <c>Schemata.Event.Foundation.Tests</c> itself uses for these two types) - this suite verifies
///     the bridge's wiring against the real bus, not Event.Foundation's own persistence layer.
/// </summary>
public class EventToMailboxShould
{
    [Fact]
    public async Task PublishAsync_RegisteredRoute_DeliversEventInstanceToResolvedActorMailboxThroughTheRealBus() {
        var services = new ServiceCollection();
        services.AddSingleton(CreateEventRepository());
        services.AddSingleton(CreateSubscriptionRepository());

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseEvent()
               .RegisterEvent<OrderPlaced>("orders/order-placed")
               .UseProducer(p => p.UseInProcess())
               .UseConsumer(c => c.UseInProcess());
        builder.UseActor(actor => {
            actor.Register<RecordingActor>("recorder");
            actor.UseEvent().RouteEvent<OrderPlaced, OrderPlacedRoute>();
        });
        builder.Invoke(services);

        await using var root = services.BuildServiceProvider();

        // Real outbox delivery: the producer only records a Pending audit row; this background
        // loop is what actually calls the consumer path (HandlerResolver -> IEventHandler<TEvent>).
        var dispatcher = root.GetRequiredService<EventOutboxDispatcher>();
        await dispatcher.StartAsync(CancellationToken.None);

        try {
            var @event = new OrderPlaced("order-1");
            await using (var scope = root.CreateAsyncScope()) {
                var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
                await bus.PublishAsync(@event);
            }

            var system = root.GetRequiredService<IActorSystem>();
            var actor  = await system.GetAsync(new ActorId("recorder", "order-1"));

            IMessage? received = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (received is null && DateTime.UtcNow < deadline) {
                received = await actor.AskAsync<GetReceived, IMessage?>(new GetReceived());
                if (received is null) {
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                }
            }

            Assert.Equal(@event, received);
        } finally {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void RouteEvent_EventTypeWithNoRegisteredRoute_RegistersNoHandler() {
        var services = new ServiceCollection();
        services.AddSchemataActor();

        var actorBuilder = new SchemataActorBuilder(new SchemataOptions(), services);
        actorBuilder.Register<RecordingActor>("recorder");
        // Only OrderPlaced is routed; OrderCancelled is never registered through RouteEvent.
        actorBuilder.UseEvent().RouteEvent<OrderPlaced, OrderPlacedRoute>();

        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        // No IEventHandler<OrderCancelled> means whatever event bus is installed has nothing to
        // invoke for this event type - it is structurally never delivered to any actor.
        var handlers = scope.ServiceProvider.GetServices<IEventHandler<OrderCancelled>>();
        Assert.Empty(handlers);
    }

    private static IRepository<SchemataEvent> CreateEventRepository() {
        var storage    = new List<SchemataEvent>();
        var repository = new Mock<IRepository<SchemataEvent>>();

        repository.Setup(r => r.AddAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
                  .Returns((SchemataEvent row, CancellationToken _) => {
                      storage.Add(row);
                      return Task.CompletedTask;
                  });
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.ListAsync(
                              It.IsAny<Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>> query,
                                   CancellationToken                                           _
                               ) => ToAsync(query(storage.AsQueryable())));

        return repository.Object;
    }

    private static IRepository<SchemataEventSubscription> CreateSubscriptionRepository() {
        var repository = new Mock<IRepository<SchemataEventSubscription>>();

        repository.Setup(r => r.ListAsync(
                              It.IsAny<Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>> query,
                                   CancellationToken                                                                  _
                               ) => ToAsync(query(Array.Empty<SchemataEventSubscription>().AsQueryable())));

        return repository.Object;
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> rows) {
        foreach (var row in rows) {
            yield return row;
        }

        await Task.CompletedTask;
    }
}
