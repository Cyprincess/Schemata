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
using Schemata.Event.Foundation.Observers;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

public class EventObserverPropagationShould
{
    private const string EventName = "sample-event";

    [Fact]
    public async Task Consumed_Observer_Failure_Propagates_After_Audit_Records_Failed() {
        SchemataEvent? row = null;
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(r => r.AddAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Callback((SchemataEvent added, CancellationToken _) => row = added)
               .Returns(Task.CompletedTask);
        records.Setup(r => r.UpdateAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var failing = new Mock<IEventLifecycleObserver>();
        failing.Setup(o => o.OnConsumedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("consume observer failed"));

        var handler = new Mock<IEventHandler<SampleEvent>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        await using var services = Base(records)
            .AddSingleton<IEventLifecycleObserver>(failing.Object)
            .AddSingleton(handler.Object)
            .BuildServiceProvider();
        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new SampleEvent()));

        Assert.Equal("consume observer failed", ex.Message);
        Assert.NotNull(row);
        Assert.Equal(EventState.Failed, row!.State);
        Assert.Equal("consume observer failed", row.RecentError);
        records.Verify(r => r.UpdateAsync(row, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static IServiceCollection Base(Mock<IRepository<SchemataEvent>> records) {
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(r => r.RequireName(typeof(SampleEvent))).Returns(EventName);
        registry.Setup(r => r.GetRouting(It.IsAny<Type>())).Returns(EventRouting.Broadcast);

        var subscriptions = new Mock<IRepository<SchemataEventSubscription>>();
        subscriptions.Setup(r => r.ListAsync(
                          It.IsAny<Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>>(),
                          It.IsAny<CancellationToken>()))
                     .Returns(EmptyAsync<SchemataEventSubscription>());

        return new ServiceCollection()
               .AddSingleton(registry.Object)
               .AddSingleton(subscriptions.Object)
               .AddSingleton<IEventDispatchContext>(new EventDispatchContext())
               .AddSingleton<HandlerResolver>()
               .AddSingleton(records.Object)
               .AddSingleton<IEventLifecycleObserver>(new SchemataEventAuditObserver(
                   records.Object, Options.Create(new JsonSerializerOptions())));
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>() {
        yield break;
    }

    public sealed class SampleEvent : IEvent;
}
