using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Core;
using Schemata.Event.Skeleton;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Event.Events;
using Schemata.Scheduling.Event.Features;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class EventPublishingJobLifecycleObserverShould
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Lifecycle_Callback_Publishes_Blocked_Or_Skipped_Event(bool blocked) {
        var events = new Mock<IEventBus>();
        events.Setup(bus => bus.PublishAsync(It.IsAny<JobBlocked>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        events.Setup(bus => bus.PublishAsync(It.IsAny<JobSkipped>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var services = new ServiceCollection().AddSingleton(events.Object).AddSingleton<IScheduledJobRegistry>(new DefaultScheduledJobRegistry());
        new SchemataSchedulingEventFeature().ConfigureServices(services, new SchemataOptions(), new Configurators(), null!, null!);
        await using var provider = services.BuildServiceProvider();
        var observer = Assert.Single(provider.GetServices<IJobLifecycleObserver>());
        var job = new SchemataJob { CanonicalName = "jobs/a" };
        var context = new JobContext { Job = "jobs/a" };

        if (blocked) {
            await observer.OnBlockedAsync(job, context);
            events.Verify(bus => bus.PublishAsync(It.Is<JobBlocked>(e => e.Job == "jobs/a"), It.IsAny<CancellationToken>()), Times.Once);
        } else {
            await observer.OnSkippedAsync(job, context);
            events.Verify(bus => bus.PublishAsync(It.Is<JobSkipped>(e => e.Job == "jobs/a"), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
