using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

/// <summary>
///     The bus lost its request/reply member; broadcast is now its whole job. Publishing must
///     work with no request dispatcher registered, so an application wanting events alone needs
///     no request dispatcher.
/// </summary>
public class EventBusWithoutMessagingShould
{
    [Fact]
    public async Task Publish_WithNoRequestDispatcherRegistered() {
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(r => r.RequireName(typeof(OrderPlaced))).Returns("order.placed");
        registry.Setup(r => r.GetRouting(It.IsAny<Type>())).Returns(EventRouting.Broadcast);

        var subscriptions = new Mock<IRepository<SchemataEventSubscription>>();
        subscriptions.Setup(r => r.ListAsync(
                          It.IsAny<Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>>(),
                          It.IsAny<CancellationToken>()))
                     .Returns(EmptyAsync<SchemataEventSubscription>());

        var observer = new Mock<IEventLifecycleObserver>();

        await using var services = new ServiceCollection()
                                  .AddSingleton(registry.Object)
                                  .AddSingleton(subscriptions.Object)
                                  .AddSingleton<IEventDispatchContext>(new EventDispatchContext())
                                  .AddSingleton<HandlerResolver>()
                                  .AddSingleton<IEventHandler<OrderPlaced>>(Mock.Of<IEventHandler<OrderPlaced>>())
                                  .AddSingleton(observer.Object)
                                  .BuildServiceProvider();

        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        await bus.PublishAsync(new OrderPlaced());

        observer.Verify(o => o.OnPublishedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>() {
        yield break;
    }

    public sealed class OrderPlaced : IEvent;
}
