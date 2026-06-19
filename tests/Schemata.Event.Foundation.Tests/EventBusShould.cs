using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

public class EventBusShould
{
    [Fact]
    public async Task PublishViaBaseType_UsesRuntimeType() {
        var registry = new DefaultEventTypeRegistry();
        registry.Register(typeof(DerivedEvent), "derived.event");

        var observer = new CapturingObserver();

        var services = new ServiceCollection();
        services.AddSingleton<IEventTypeRegistry>(registry);
        services.AddScoped<IEventSubscriptionStore, InMemoryEventSubscriptionStore>();
        services.AddScoped<HandlerResolver>();
        services.AddScoped<IEventDispatchContext, EventDispatchContext>();
        services.AddSingleton(Options.Create(new SchemataEventOptions()));
        services.AddSingleton<IEventLifecycleObserver>(observer);
        services.AddSingleton<IEventHandler<IEvent>>(new NoopHandler());
        var sp = services.BuildServiceProvider();

        var bus = new InProcessEventBus(sp, Options.Create(new JsonSerializerOptions()));

        await bus.PublishAsync<ITestEvent>(new DerivedEvent { Value = "payload" });

        Assert.NotNull(observer.Captured);
        Assert.Equal("derived.event", observer.Captured!.EventType);
        Assert.Contains("payload", observer.Captured.Payload);
    }

    [Fact]
    public async Task PublishAsync_DoesNotInvokeHandlersBeforeOutboxDispatch() {
        var registry = new DefaultEventTypeRegistry();
        registry.Register(typeof(DerivedEvent), "derived.event");

        var handler = new CountingHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IEventTypeRegistry>(registry);
        services.AddScoped<IEventSubscriptionStore, InMemoryEventSubscriptionStore>();
        services.AddScoped<HandlerResolver>();
        services.AddScoped<IEventDispatchContext, EventDispatchContext>();
        services.AddSingleton(Options.Create(new SchemataEventOptions()));
        services.AddSingleton<IEventLifecycleObserver>(new CapturingObserver());
        services.AddSingleton<IEventHandler<IEvent>>(handler);
        var sp = services.BuildServiceProvider();

        var bus = new InProcessEventBus(sp, Options.Create(new JsonSerializerOptions()));

        await bus.PublishAsync<ITestEvent>(new DerivedEvent { Value = "payload" });

        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task PublishAsync_WithSource_PropagatesSourceToObserver() {
        var registry = new DefaultEventTypeRegistry();
        registry.Register(typeof(DerivedEvent), "derived.event");

        var observer = new CapturingObserver();
        var source   = new SourceEntity { CanonicalName = "widgets/1", Timestamp = Identifiers.NewUid() };

        var services = new ServiceCollection();
        services.AddSingleton<IEventTypeRegistry>(registry);
        services.AddScoped<IEventSubscriptionStore, InMemoryEventSubscriptionStore>();
        services.AddScoped<HandlerResolver>();
        services.AddScoped<IEventDispatchContext, EventDispatchContext>();
        services.AddSingleton(Options.Create(new SchemataEventOptions()));
        services.AddSingleton<IEventLifecycleObserver>(observer);
        services.AddSingleton<IEventHandler<IEvent>>(new NoopHandler());
        var sp = services.BuildServiceProvider();

        IEventBus bus = new InProcessEventBus(sp, Options.Create(new JsonSerializerOptions()));

        await bus.PublishAsync<ITestEvent>(new DerivedEvent { Value = "payload" }, source);

        Assert.Same(source, observer.Captured?.Source);
    }

    #region Nested type: CapturingObserver

    private sealed class CapturingObserver : IEventLifecycleObserver
    {
        public EventContext? Captured { get; private set; }

        #region IEventLifecycleObserver Members

        public Task OnPublishedAsync(EventContext context, CancellationToken ct = default) {
            Captured = context;
            return Task.CompletedTask;
        }

        public Task OnConsumedAsync(EventContext context, CancellationToken ct = default) { return Task.CompletedTask; }

        #endregion
    }

    #endregion

    #region Nested type: CountingHandler

    private sealed class CountingHandler : IEventHandler<IEvent>
    {
        public int Count { get; private set; }

        #region IEventHandler<IEvent> Members

        public Task HandleAsync(IEvent @event, CancellationToken ct = default) {
            Count++;
            return Task.CompletedTask;
        }

        #endregion
    }

    #endregion

    #region Nested type: DerivedEvent

    private sealed class DerivedEvent : ITestEvent
    {
        public string? Value { get; set; }
    }

    #endregion

    #region Nested type: ITestEvent

    private interface ITestEvent : IEvent;

    #endregion

    #region Nested type: NoopHandler

    private sealed class NoopHandler : IEventHandler<IEvent>
    {
        #region IEventHandler<IEvent> Members

        public Task HandleAsync(IEvent @event, CancellationToken ct = default) { return Task.CompletedTask; }

        #endregion
    }

    #endregion

    #region Nested type: SourceEntity

    private sealed class SourceEntity : ICanonicalName, IConcurrency
    {
        #region ICanonicalName Members

        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        #endregion

        #region IConcurrency Members

        public Guid Timestamp { get; set; }

        #endregion
    }

    #endregion
}
