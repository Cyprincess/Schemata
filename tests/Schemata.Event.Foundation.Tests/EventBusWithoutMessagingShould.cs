using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

/// <summary>
///     The bus lost its request/reply member; broadcast is now its whole job. These pin that the
///     removal did not take publish down with it, and that an application wanting events alone needs
///     no request dispatcher registered.
/// </summary>
public class EventBusWithoutMessagingShould
{
    [Fact]
    public async Task Publish_WithNoRequestDispatcherRegistered() {
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(r => r.RequireName(typeof(OrderPlaced))).Returns("order.placed");

        var observer = new Mock<IEventLifecycleObserver>();

        await using var services = new ServiceCollection()
                                  .AddSingleton(registry.Object)
                                  .AddSingleton(observer.Object)
                                  .BuildServiceProvider();

        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        await bus.PublishAsync(new OrderPlaced());

        observer.Verify(o => o.OnPublishedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Expose_NoRequestDispatchMember() {
        // Guards the split structurally: a re-added SendAsync would restore the coupling that made
        // every request/reply consumer depend on the event domain.
        Assert.DoesNotContain(typeof(IEventBus).GetMethods(), m => m.Name == "SendAsync");
    }

    [Fact]
    public void Keep_EventsAsMessages() {
        // IEvent still flows anywhere IMessage is accepted — an actor mailbox, for instance —
        // without the event domain gaining a dependency in return.
        Assert.True(typeof(IMessage).IsAssignableFrom(typeof(IEvent)));
    }

    private sealed class OrderPlaced : IEvent;
}
