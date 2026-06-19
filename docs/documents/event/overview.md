# Event

The event subsystem is a publish/subscribe and request/reply bus with an explicit wire-name
registry, a transactional outbox, an advisor pipeline for publish and consume hooks, and pluggable
transport backends. Every event type carries a wire name registered through
`Schemata.Event.Skeleton.IEventTypeRegistry`. That name is what the transport routes on, what
`EventContext.EventType` exposes, and what the `SchemataEvent.EventType` audit column stores — one
string end to end. Publishing a type with no registration throws at the publish call.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Event.Skeleton` | `IEventBus.cs`, `IEvent.cs`, `IRequest.cs`, `IEventHandler.cs`, `IRequestHandler.cs`, `IEventTypeRegistry.cs`, `EventContext.cs`, `EventRouting.cs`, `IEventLifecycleObserver.cs`, `IEventOutboxPublisher.cs`, `EventOutboxMessage.cs`, `EventOutboxDelivery.cs`, `IEventSubscriptionStore.cs`, `IEventSubscription.cs`, `EventSubscription.cs`, `IEventDispatchContext.cs`, `Entities/SchemataEvent.cs`, `Entities/EventState.cs`, `Entities/SchemataEventSubscription.cs`, `Advisors/IEventPublishAdvisor.cs`, `Advisors/IEventConsumeAdvisor.cs` |
| `Schemata.Event.Foundation` | `Features/SchemataEventFeature.cs`, `Builders/EventBuilder.cs`, `Builders/EventProducerBuilder.cs`, `Builders/EventConsumerBuilder.cs`, `Extensions/SchemataBuilderExtensions.cs`, `Observers/SchemataEventAuditObserver.cs`, `EventOutboxDispatcher.cs`, `Internal/InProcessEventBus.cs`, `Internal/InProcessEventOutboxPublisher.cs`, `Internal/DefaultEventTypeRegistry.cs`, `Internal/RepositoryEventSubscriptionStore.cs`, `Internal/HandlerResolver.cs` |
| `Schemata.Event.RabbitMq` | `RabbitMqEventOptions.cs`, `Internal/RabbitMqEventBus.cs`, `Internal/RabbitMqConsumerHost.cs`, `Internal/RabbitMqEventOutboxPublisher.cs`, `Internal/CorrelationTracker.cs`, `Extensions/EventProducerBuilderRabbitMqExtensions.cs`, `Extensions/EventConsumerBuilderRabbitMqExtensions.cs` |

## Wire names

A wire name is the application-configured string that publishers and consumers route on,
independent of the CLR namespace of the type that carries it. `IEventTypeRegistry` forces an
explicit name per type so the wire shape survives type renames, refactors, and cross-service
deployments. `RequireName(type)` resolves `Type → wireName` at publish time and throws
`InvalidOperationException` for an unregistered type. The default registry,
`DefaultEventTypeRegistry`, also rejects a second registration that would map a type to a different
name or a name to a different type.

The bus resolves the name from the runtime type (`@event.GetType()`), so an event published through
a base type or interface still routes under its own registered name and serializes its derived
members.

## Startup

`UseEvent()` on `SchemataBuilder` activates `Schemata.Event.Foundation.Features.SchemataEventFeature`
(Priority `Orders.Extension + 40_000_000` = 440,000,000) and returns an `EventBuilder`. Configure by
chaining on the returned builder:

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

1. `IEventTypeRegistry` as a singleton, built from the accumulated `EventTypeRegistryConfiguration`.
2. `SchemataEventAuditObserver` as a scoped `IEventLifecycleObserver` (`TryAddEnumerable`).
3. `InProcessEventOutboxPublisher` as the default `IEventOutboxPublisher` singleton.
4. `EventOutboxDispatcher` as a singleton and a hosted service.

The `IEventPublishAdvisor` and `IEventConsumeAdvisor` sockets stay open for application pipelines;
no built-in advisor ships.

## EventBuilder

`Schemata.Event.Foundation.Builders.EventBuilder` is the fluent configuration surface:

| Member | Effect |
| --- | --- |
| `RegisterEvent<TEvent>(string name)` | Maps the CLR type to a wire name via `IPostConfigureOptions<EventTypeRegistryConfiguration>`. |
| `UseProducer(Action<EventProducerBuilder>?)` | Configures the producer (the `IEventBus` implementation). |
| `UseConsumer(Action<EventConsumerBuilder>?)` | Configures the consumer (subscription store, handler resolver, dispatch context). |
| `UseHandler<TEvent, THandler>()` | Registers a scoped `IEventHandler<TEvent>`. |
| `UseHandler<TRequest, TResponse, THandler>()` | Registers a scoped `IRequestHandler<TRequest, TResponse>`. |
| `ConfigureRouting<TEvent>(EventRouting)` | Sets the per-type `EventRouting` mode. |

## IEventBus

```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent;

    Task PublishAsync<TEvent>(TEvent @event, object sourceEntity, CancellationToken ct = default)
        where TEvent : IEvent;

    Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>;
}
```

`PublishAsync` is fire-and-forget (one-to-many). It does not run handlers inline: it records an
outbox audit row and returns. The `EventOutboxDispatcher` drains that row and invokes handlers. See
[Dispatch Pipeline](dispatch-pipeline.md).

`SendAsync` is request/reply (one-to-one) and runs the single registered handler inline, returning
its response.

The `(@event, sourceEntity, ct)` overload attaches an originating business entity to the publish.
`sourceEntity` must implement both `Schemata.Abstractions.Entities.ICanonicalName` and
`IConcurrency`; the default interface method `EnsureSourceEntityContract` throws
`InvalidOperationException` naming the offending type otherwise. The audit observer captures the
source's `CanonicalName` and concurrency `Timestamp` onto the `SchemataEvent` row so consumers can
compare the publish snapshot against the source's current state.

All three calls require the type registered in `IEventTypeRegistry` first.

## EventContext

`Schemata.Event.Skeleton.EventContext` is the per-dispatch carrier passed to advisors and observers:

| Member | Description |
| --- | --- |
| `IEvent Event` | The dispatched event instance. |
| `string EventType` | Wire-format name; also the routing key and the persisted `SchemataEvent.EventType`. |
| `string? Payload` | Serialized event body for audit and transport. |
| `string? CorrelationId` | End-to-end correlation identifier. |
| `SchemataEvent? Record` | Audit row attached by the audit observer on publish. |
| `bool RequiresOutboxDelivery` | Set by the bus when delivery runs through a durable broker; drives the initial audit state. |
| `object? Source` | Optional originating business entity from the source-entity overload. |
| `object? Result` | Handler outcome (request response or publish acknowledgement). |
| `Exception? Exception` | Exception thrown by the handler, if any. |

## SchemataEvent audit entity

`Schemata.Event.Skeleton.Entities.SchemataEvent` (`[Table("SchemataEvents")]`,
`[CanonicalName("events/{event}")]`) implements `IIdentifier`, `ICanonicalName`, `IConcurrency`,
`ISourceReference`, and `ITimestamp`:

| Column | Description |
| --- | --- |
| `EventType` | Wire-format name. |
| `Payload` | Serialized event body. |
| `State` | `EventState` lifecycle value. |
| `CorrelationId` | Correlation identifier copied from the context. |
| `ResponsePayload` | Serialized handler response (request/reply). |
| `RecentError` | Last error from a failed handler dispatch. |
| `RetryCount` | Number of outbox redelivery attempts. |
| `SourceType` | CLR full name of the source business entity. |
| `Source` | Canonical name of the source entity. |
| `SourceTimestamp` | Concurrency token captured from the source at publish time. |

### EventState

```csharp
public enum EventState
{
    Recorded   = 0,  // accepted by the transport or dispatched in-process; awaiting consume
    Succeeded  = 1,  // the handler completed successfully
    Failed     = 2,  // the handler threw
    Pending    = 3,  // outbox row awaiting broker delivery or retry
    Publishing = 4,  // an outbox dispatcher has claimed the row and is publishing it
}
```

Ordinals are persisted; the enum is append-only.

## EventRouting

```csharp
public enum EventRouting
{
    Broadcast,           // every matched handler receives the event
    CompetingConsumers,  // exactly one matched handler receives the event
}
```

The default is `Broadcast`. Set per type with `EventBuilder.ConfigureRouting<TEvent>(routing)`,
stored in `SchemataEventOptions.RoutingTable`.

## Extension points

- Implement `IEventPublishAdvisor` (`TryAddEnumerable`) for publish-time hooks: header injection,
  encryption, or short-circuit via `Block`/`Handle`.
- Implement `IEventConsumeAdvisor` (`TryAddEnumerable`) for consume-time hooks: metrics or
  dead-letter routing.
- Implement `IEventLifecycleObserver` (`TryAddEnumerable`) to react to settled publish/deliver/consume
  transitions alongside the built-in audit observer.
- Implement `IEventBus` (scoped) to replace the transport.
- Implement `IEventOutboxPublisher` (singleton) to replay outbox rows over a custom broker.
- Implement `IEventSubscriptionStore` to back durable subscriptions with a different store.

## Caveats

- `PublishAsync` does not invoke handlers before it returns. Handlers run when the outbox dispatcher
  drains the row, so delivery is asynchronous and at-least-once; handlers must be idempotent.
- `RequireName(type)` throws for unregistered types. Register every event and request type used in
  `PublishAsync`/`SendAsync` during startup.
- The source-entity overload throws before publishing if the source does not implement both
  `ICanonicalName` and `IConcurrency`.
- `SchemataEvent.EventType` stores the wire name. Queries against this column key on that string,
  not on a CLR type.

## See also

- [Dispatch Pipeline](dispatch-pipeline.md)
- [Providers](providers.md)
- [Scheduling Event Integration](../scheduling/event-integration.md)
