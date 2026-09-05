using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
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

        var observer = new Mock<IEventLifecycleObserver>();

        await using var services = new ServiceCollection()
                                  .AddSingleton(registry.Object)
                                  .AddSingleton(observer.Object)
                                  .BuildServiceProvider();

        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        await bus.PublishAsync(new OrderPlaced());

        observer.Verify(o => o.OnPublishedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }


    private sealed class OrderPlaced : IEvent;
}
