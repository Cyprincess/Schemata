# Event

The event subsystem provides a publish/subscribe bus with an explicit wire-name registry, an advisor pipeline for publish and consume hooks, and pluggable transport backends. Every event type carries a wire name registered through `IEventTypeRegistry`; that name is what the transport routes on, what `EventContext.EventType` exposes, and what the `SchemataEvent.EventType` audit column stores. Publishing a type with no registration fails fast at `PublishAsync`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Event.Skeleton` | `IEventBus.cs`, `IEvent.cs`, `IRequest.cs`, `IEventHandler.cs`, `IRequestHandler.cs`, `IEventTypeRegistry.cs`, `IEventSubscriptionStore.cs`, `IEventSubscription.cs`, `IEventDispatchContext.cs`, `IEventLifecycleObserver.cs`, `EventContext.cs`, `EventRouting.cs`, `Entities/SchemataEvent.cs`, `Entities/EventState.cs`, `Advisors/IEventPublishAdvisor.cs`, `Advisors/IEventConsumeAdvisor.cs` |
| `Schemata.Event.Foundation` | `Features/SchemataEventFeature.cs`, `Builders/EventBuilder.cs`, `Builders/EventProducerBuilder.cs`, `Builders/EventConsumerBuilder.cs`, `Extensions/SchemataBuilderExtensions.cs`, `Observers/SchemataEventAuditObserver.cs`, `Internal/InProcessEventBus.cs`, `Internal/DefaultEventTypeRegistry.cs`, `Internal/InMemoryEventSubscriptionStore.cs`, `Internal/EventDispatchContext.cs`, `Internal/HandlerResolver.cs` |
| `Schemata.Event.RabbitMq` | `RabbitMqEventOptions.cs`, `Internal/RabbitMqEventBus.cs`, `Internal/RabbitMqConsumerHost.cs`, `Extensions/EventProducerBuilderRabbitMqExtensions.cs`, `Extensions/EventConsumerBuilderRabbitMqExtensions.cs` |

## Wire names

The wire name is the application-configured string that publishers and consumers use to route a payload, independent of the CLR namespace of the type that carries it. The registry resolves `Type → wireName` at publish time via `IEventTypeRegistry.RequireName(type)`, which throws `InvalidOperationException` if the type was never registered.

```csharp
// EventContext constructor:
public EventContext(IEvent @event, string eventType) {
    Event     = @event;
    EventType = eventType;  // wire name from IEventTypeRegistry
}
```

## Startup

`UseEvent` on `SchemataBuilder` activates `SchemataEventFeature` (Priority `Orders.Extension + 40_000_000` = 440,000,000) and returns an `EventBuilder` for fluent configuration. Configuration is done by chaining methods on the returned builder:

```csharp
builder.UseSchemata(schema => {
    schema.UseEvent()
          .RegisterEvent<OrderPlaced>("orders/order-placed")
          .UseProducer(p => p.UseInProcess())
          .UseConsumer(c => c.UseInProcess())
          .UseHandler<OrderPlaced, OrderPlacedHandler>();
});
```

`SchemataEventFeature.ConfigureServices` registers:

1. `IEventTypeRegistry` as a singleton, built from `EventTypeRegistryConfiguration` options.
2. `SchemataEventAuditObserver` as a scoped `IEventLifecycleObserver` via `TryAddEnumerable`.

Producer, consumer, and handler registrations are added by `UseProducer`, `UseConsumer`, and `UseHandler` on `EventBuilder`. `IEventPublishAdvisor` and `IEventConsumeAdvisor` sockets are left open for application pipelines; no built-in advisor ships in the box.

## EventBuilder

`EventBuilder` is returned by `UseEvent()` and provides the fluent configuration surface:

```csharp
EventBuilder RegisterEvent<TEvent>(string name)
EventBuilder UseProducer(Action<EventProducerBuilder>? configure = null)
EventBuilder UseConsumer(Action<EventConsumerBuilder>? configure = null)
EventBuilder UseHandler<TEvent, THandler>()
EventBuilder UseHandler<TRequest, TResponse, THandler>()
EventBuilder ConfigureRouting<TEvent>(EventRouting routing)
```

`RegisterEvent<TEvent>(name)` registers the wire name via `IPostConfigureOptions<EventTypeRegistryConfiguration>`. The name is stored in `IEventTypeRegistry` at startup.

## IEventBus

```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent;

    Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>;
}
```

`PublishAsync` is fire-and-forget (one-to-many). `SendAsync` is request/reply (one-to-one). Both require the type to be registered in `IEventTypeRegistry` before calling.

## EventContext

```csharp
public class EventContext
{
    public IEvent   Event           { get; }
    public string   EventType       { get; }  // wire name
    public string?  Payload         { get; set; }
    public string?  CorrelationId   { get; set; }
    public SchemataEvent? Record    { get; set; }
    public object?  Result          { get; set; }
    public Exception? Exception     { get; set; }
}
```

`SchemataEventAuditObserver` writes `EventType` (and the rest of the context) into `SchemataEvent.EventType` so the audit table and the runtime carry the same identifier.

## SchemataEvent audit entity

```csharp
[Table("SchemataEvents")]
[CanonicalName("events/{event}")]
public class SchemataEvent : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    public string?     EventType       { get; set; }  // wire name
    public string?     Payload         { get; set; }
    public EventState  State           { get; set; }
    public string?     CorrelationId   { get; set; }
    public string?     ResponsePayload { get; set; }
    public string?     RecentError     { get; set; }
    public int         RetryCount      { get; set; }
}
```

`EventState` values: `Recorded`, `Delivered`, `Failed`.

## EventRouting

```csharp
public enum EventRouting
{
    Broadcast,           // all handlers receive the event
    CompetingConsumers,  // only one handler receives the event
}
```

Default is `Broadcast`. Configure per-type via `EventBuilder.ConfigureRouting<TEvent>(routing)`.

## Extension points

- Implement `IEventPublishAdvisor` and register via `TryAddEnumerable` to add publish-time hooks (e.g., encryption, header injection).
- Implement `IEventConsumeAdvisor` and register via `TryAddEnumerable` to add consume-time hooks (e.g., metrics, dead-letter routing).
- Implement `IEventBus` and register as scoped to replace the transport entirely.
- Implement `IEventSubscriptionStore` to persist subscriptions in a database.

## Caveats

- `UseEvent()` returns `EventBuilder` directly and takes no delegate. Configuration happens by chaining on the returned builder.
- `IEventTypeRegistry.RequireName(type)` throws for unregistered types. Register every event and request type used in `PublishAsync`/`SendAsync` during startup.
- `SchemataEvent.EventType` stores the wire name. Queries against this column key on that string, not on a CLR type.

## See also

- [Dispatch Pipeline](dispatch-pipeline.md)
- [Providers](providers.md)
- [Scheduling Event Integration](../scheduling/event-integration.md)
- [Flow Event Integration](../flow/event.md)
