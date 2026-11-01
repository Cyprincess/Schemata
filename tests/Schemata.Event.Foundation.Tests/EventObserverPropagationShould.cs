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
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;
using Schemata.Abstractions.Entities;
using Schemata.Event.RabbitMq;
using Schemata.Event.RabbitMq.Runtime;
using Schemata.Transport.RabbitMq;

namespace Schemata.Event.Foundation.Tests;

public class EventObserverPropagationShould
{
    private const string EventName = "sample-event";

    [Fact]
    public async Task Consumed_Observer_Failure_Propagates_After_Audit_Records_Failed() {
        SchemataEvent? row = null;
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(r => r.AddAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
               .Callback((SchemataEvent added, CancellationToken _, MutationOptions _) => row = added)
               .ReturnsAsync(MutationResult.Applied);
        records.Setup(r => r.UpdateAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
               .ReturnsAsync(MutationResult.Applied);
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
        records.Verify(r => r.UpdateAsync(row, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Published_Observer_Failure_Propagates_Before_Handler_Dispatch() {
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(repository => repository.AddAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
               .ReturnsAsync(MutationResult.Applied);
        records.Setup(repository => repository.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var failure = new InvalidOperationException("publish observer failed");
        var observer = new Mock<IEventLifecycleObserver>();
        observer.Setup(value => value.OnPublishedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(failure);
        var handler = new Mock<IEventHandler<SampleEvent>>(MockBehavior.Strict);
        await using var services = Base(records).AddSingleton(observer.Object).AddSingleton(handler.Object)
                                               .BuildServiceProvider();

        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new SampleEvent())));
        handler.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Integration")]
    public async Task SourceBearing_Publish_Preserves_The_Source_For_Observers(bool correlated) {
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(value => value.AddAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
               .ReturnsAsync(MutationResult.Applied);
        records.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var source = new Mock<ICanonicalName>();
        source.As<IConcurrency>();
        EventContext? published = null;
        var observer = new Mock<IEventLifecycleObserver>();
        observer.Setup(value => value.OnPublishedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()))
                .Callback<EventContext, CancellationToken>((context, _) => published = context).Returns(Task.CompletedTask);
        await using var services = Base(records).AddSingleton(observer.Object).BuildServiceProvider();
        IEventBus bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        if (correlated) await bus.PublishAsync(new SampleEvent(), source.Object, new Dictionary<string, string> { ["order"] = "o1" });
        else await bus.PublishAsync(new SampleEvent(), source.Object);

        Assert.NotNull(published);
        Assert.Same(source.Object, published.Source);
        if (correlated) Assert.Equal("o1", published.Correlation!["order"]);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task RabbitMq_Published_Observer_Failure_Propagates_Before_Opening_Connection() {
        var failure = new InvalidOperationException("publish observer failed");
        var observer = new Mock<IEventLifecycleObserver>();
        observer.Setup(value => value.OnPublishedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(failure);
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(value => value.RequireName(typeof(SampleEvent))).Returns(EventName);
        var connections = new Mock<IRabbitMqConnectionProvider>(MockBehavior.Strict);
        await using var services = new ServiceCollection().AddSingleton(observer.Object)
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>().BuildServiceProvider();
        var bus = new RabbitMqEventBus(Options.Create(new RabbitMqEventOptions()), connections.Object,
            registry.Object, services, Options.Create(new JsonSerializerOptions()));

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new SampleEvent())));
        connections.VerifyNoOtherCalls();
    }

    private static IServiceCollection Base(Mock<IRepository<SchemataEvent>> records) {
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(r => r.RequireName(typeof(SampleEvent))).Returns(EventName);
        registry.Setup(r => r.GetRouting(It.IsAny<Type>())).Returns(EventRouting.Broadcast);

        var subscriptions = new Mock<IRepository<SchemataEventSubscription>>();
        subscriptions.Setup(r => r.ListAsync(
                          It.IsAny<Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>>(),
                          It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                     .Returns(EmptyAsync<SchemataEventSubscription>());

        return new ServiceCollection()
               .AddSingleton(registry.Object)
               .AddSingleton(subscriptions.Object)
               .AddSingleton<IEventDispatchContext>(new EventDispatchContext())
               .AddSingleton<HandlerResolver>()
               .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
               .AddSingleton(records.Object)
               .AddSingleton<IEventLifecycleObserver>(new SchemataEventAuditObserver(
                   records.Object, Options.Create(new JsonSerializerOptions())));
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>() {
        yield break;
    }

    public sealed class SampleEvent : IEvent;
}
