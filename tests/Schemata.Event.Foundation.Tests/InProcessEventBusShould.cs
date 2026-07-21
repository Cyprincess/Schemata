using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

public class InProcessEventBusShould
{
    [Fact]
    public async Task Publish_Isolates_Lifecycle_Observer_Failure_And_Logs_Warning() {
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(r => r.RequireName(typeof(SampleEvent))).Returns("sample");

        var throwing = new Mock<IEventLifecycleObserver>();
        throwing.Setup(o => o.OnPublishedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("observer boom"));
        var trailing = new Mock<IEventLifecycleObserver>();

        var logger = new Mock<ILogger<InProcessEventBus>>();

        await using var services = new ServiceCollection()
                                  .AddSingleton(registry.Object)
                                  .AddSingleton(throwing.Object)
                                  .AddSingleton(trailing.Object)
                                  .BuildServiceProvider();

        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()), logger.Object);

        await bus.PublishAsync(new SampleEvent());

        trailing.Verify(o => o.OnPublishedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()), Times.Once);
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private sealed class SampleEvent : IEvent;
}
