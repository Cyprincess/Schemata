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
using Schemata.Event.Foundation.Observers;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

public class EventObserverPropagationShould
{
    private const string EventName = "sample-event";

    [Fact]
    public async Task Consumed_Observer_Failure_Propagates_After_Audit_Records_Failed() {
        var record = new SchemataEvent {
            EventType = EventName, CorrelationId = "corr-1", State = EventState.Pending,
        };

        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(r => r.FirstOrDefaultAsync<SchemataEvent>(
                      It.IsAny<Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>>>(),
                      It.IsAny<CancellationToken>()))
               .Returns(ValueTask.FromResult<SchemataEvent?>(record));
        records.Setup(r => r.UpdateAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var failing = new Mock<IEventLifecycleObserver>();
        failing.Setup(o => o.OnConsumedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("consume observer failed"));

        await using var services = Base()
            .AddSingleton<IEventLifecycleObserver>(new SchemataEventAuditObserver(
                records.Object, Options.Create(new JsonSerializerOptions())))
            .AddSingleton<IEventLifecycleObserver>(failing.Object)
            .AddSingleton<IRepository<SchemataEvent>>(records.Object)
            .AddSingleton<IEventHandler<SampleEvent>>(Mock.Of<IEventHandler<SampleEvent>>())
            .BuildServiceProvider();

        var publisher = new InProcessEventOutboxPublisher(services, Options.Create(new JsonSerializerOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(new(EventName, "{}", "corr-1")));

        Assert.Equal("consume observer failed", ex.Message);
        Assert.Equal(EventState.Failed, record.State);
        Assert.Equal("consume observer failed", record.RecentError);
        // The delivered pass transitions the row first; the consume pass is the failing update.
        records.Verify(r => r.UpdateAsync(record, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        records.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Delivered_Observer_Failure_Propagates_Before_Handler_Invocation() {
        var record = new SchemataEvent {
            EventType = EventName, CorrelationId = "corr-2", State = EventState.Pending,
        };
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(r => r.FirstOrDefaultAsync<SchemataEvent>(
                      It.IsAny<Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>>>(),
                      It.IsAny<CancellationToken>()))
               .Returns(ValueTask.FromResult<SchemataEvent?>(record));
        records.Setup(r => r.UpdateAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        records.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new Mock<IEventHandler<SampleEvent>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        await using var services = Base()
            .AddSingleton<IEventLifecycleObserver>(new SchemataEventAuditObserver(
                records.Object, Options.Create(new JsonSerializerOptions())))
            .AddSingleton<IEventLifecycleObserver>(new ThrowingDeliveredObserver())
            .AddSingleton<IRepository<SchemataEvent>>(records.Object)
            .AddSingleton<IEventHandler<SampleEvent>>(handler.Object)
            .BuildServiceProvider();

        var publisher = new InProcessEventOutboxPublisher(services, Options.Create(new JsonSerializerOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(new(EventName, "{}", "corr-2")));

        Assert.Equal("delivered observer failed", ex.Message);
        handler.Verify(h => h.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(EventState.Pending, record.State);
        records.Verify(r => r.UpdateAsync(record, It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IServiceCollection Base() {
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(r => r.Resolve(EventName)).Returns(typeof(SampleEvent));
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
               .AddSingleton<HandlerResolver>();
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>() {
        yield break;
    }

    public sealed class SampleEvent : IEvent;

    // OnDeliveredAsync is a default interface member, which Moq proxies do not intercept.
    private sealed class ThrowingDeliveredObserver : IEventLifecycleObserver
    {
        public Task OnPublishedAsync(EventContext context, CancellationToken ct = default) => Task.CompletedTask;

        public Task OnDeliveredAsync(EventContext context, CancellationToken ct = default) =>
            throw new InvalidOperationException("delivered observer failed");

        public Task OnConsumedAsync(EventContext context, CancellationToken ct = default) => Task.CompletedTask;
    }
}
