# Event

The event subsystem is a broadcast bus with an explicit wire-name registry, publish and consume
advisors, lifecycle audit records, and pluggable transport backends. In-process publishing awaits
handlers inline; RabbitMQ publishing awaits broker confirmation. Every event type carries a wire
name registered through `Schemata.Event.Skeleton.IEventTypeRegistry`. The transport, `EventContext`,
and `SchemataEvent.EventType` use that same name. Publishing an unregistered type throws.

## Where the code lives

| Package                     | Key files                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| --------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Messaging.Skeleton` | `IMessage.cs`, `IRequest.cs`, `IRequestHandler.cs`, `IRequestDispatcher.cs`, `MessageContext.cs`, `IMessageContextPropagator.cs`, `MessageContexts.cs` |
| `Schemata.Event.Skeleton` | `IEventBus.cs`, `IEvent.cs`, `IEventHandler.cs`, `IHasPendingEvents.cs`, `IEventTypeRegistry.cs`, `EventContext.cs`, `EventRouting.cs`, `IEventLifecycleObserver.cs`, `EventSourceContract.cs`, `IEventDispatchContext.cs`, `Entities/SchemataEvent.cs`, `Entities/EventState.cs`, `Entities/SchemataEventSubscription.cs`, `Advisors/IEventPublishAdvisor.cs`, `Advisors/IEventConsumeAdvisor.cs` |
| `Schemata.Event.Foundation` | `Features/SchemataEventFeature.cs`, `Builders/EventBuilder.cs`, `Builders/EventProducerBuilder.cs`, `Builders/EventConsumerBuilder.cs`, `Extensions/SchemataBuilderExtensions.cs`, `Observers/SchemataEventAuditObserver.cs`, `Runtime/InProcessEventBus.cs`, `Runtime/DefaultEventTypeRegistry.cs`, `SchemataEventSubscriptionExtensions.cs`, `Runtime/HandlerResolver.cs` |
| `Schemata.Event.RabbitMq` | `RabbitMqEventOptions.cs`, `Runtime/RabbitMqEventBus.cs`, `Runtime/RabbitMqConsumerHost.cs`, `Extensions/EventProducerBuilderRabbitMqExtensions.cs`, `Extensions/EventConsumerBuilderRabbitMqExtensions.cs` |
| `Schemata.Transport.RabbitMq` | `RabbitMqConnectionOptions.cs`, `IRabbitMqConnectionProvider.cs`, `CorrelationTracker.cs`, `Runtime/RabbitMqConnectionProvider.cs`, `Extensions/ServiceCollectionExtensions.cs` |

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
using Microsoft.AspNetCore.Builder;

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

The `IEventPublishAdvisor` and `IEventConsumeAdvisor` sockets stay open for application pipelines;
no built-in advisor ships.

## EventBuilder

`Schemata.Event.Foundation.Builders.EventBuilder` is the fluent configuration surface:

| Member                                        | Effect                                                                                        |
| --------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `RegisterEvent<TEvent>(string name)`          | Maps the CLR type to a wire name via `IPostConfigureOptions<EventTypeRegistryConfiguration>`. |
| `UseProducer(Action<EventProducerBuilder>?)`  | Configures the producer (the `IEventBus` implementation).                                     |
| `UseConsumer(Action<EventConsumerBuilder>?)`  | Configures the consumer (subscription store, handler resolver, dispatch context).             |
| `UseHandler<TEvent, THandler>()`              | Registers a scoped `IEventHandler<TEvent>` via `TryAddEnumerable`; multiple handlers per event type coexist.   |
| `ConfigureRouting<TEvent>(EventRouting)`      | Sets the per-type `EventRouting` mode.                                                        |

## IEventBus

```csharp
using System.Threading;
using System.Threading.Tasks;
using Schemata.Event.Skeleton;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent;

    Task PublishAsync<TEvent>(TEvent @event, object sourceEntity, CancellationToken ct = default)
        where TEvent : IEvent
    {
        EventSourceContract.Ensure(sourceEntity);
        return PublishAsync(@event, ct);
    }

}
```

**The bus is broadcast-only.** `IEvent` extends `IMessage`, and request/reply — `IRequest<TResponse>`,
`IRequestHandler<TRequest, TResponse>`, `IRequestDispatcher` — lives in `Schemata.Messaging.Skeleton`
instead. A request is a message, not an event, so request/reply is usable without taking on the event
domain at all. See [Messaging](../messaging/overview.md).

`PublishAsync` awaits the selected provider: in-process handlers finish before it returns, while
RabbitMQ confirms broker acceptance without waiting for consumers. The bus supplies neither a
transactional outbox nor automatic publish retries. An audit row is a lifecycle record, not a
durable delivery work item. See [Dispatch Pipeline](dispatch-pipeline.md).

The `(@event, sourceEntity, ct)` overload attaches an originating business entity to the publish.
`sourceEntity` must implement both `Schemata.Abstractions.Entities.ICanonicalName` and
`IConcurrency`; `EventSourceContract.Ensure` throws `InvalidOperationException` naming the
offending type otherwise. The audit observer captures the
source's `CanonicalName` and concurrency `Timestamp` onto the `SchemataEvent` row so consumers can
compare the publish snapshot against the source's current state.

Both overloads require the event type registered in `IEventTypeRegistry` first.

## EventContext

`Schemata.Event.Skeleton.EventContext` is the per-dispatch carrier passed to advisors and observers:

| Member                        | Description                                                                                 |
| ----------------------------- | ------------------------------------------------------------------------------------------- |
| `IEvent Event`                | The dispatched event instance.                                                              |
| `string EventType`            | Wire-format name; also the routing key and the persisted `SchemataEvent.EventType`.         |
| `string? Payload`             | Serialized event body for audit and transport.                                              |
| `string? CorrelationId`       | End-to-end correlation identifier.                                                          |
| `SchemataEvent? Record`       | Audit row attached by the audit observer on publish.                                        |
| `object? Source`              | Optional originating business entity from the source-entity overload.                       |
| `object? Result` | Consume outcome (`true` after successful dispatch) or a publish-advisor result. |
| `Exception? Exception` | Handler or consume-observer failure captured during dispatch. |

## SchemataEvent audit entity

`Schemata.Event.Skeleton.Entities.SchemataEvent` (`[Table("SchemataEvents")]`,
`[CanonicalName("events/{event}")]`) implements `IIdentifier`, `ICanonicalName`, `IConcurrency`,
`ISourceReference`, and `ITimestamp`:

| Column            | Description                                                 |
| ----------------- | ----------------------------------------------------------- |
| `EventType`       | Wire-format name.                                           |
| `Payload`         | Serialized event body.                                      |
| `State`           | `EventState` lifecycle value.                               |
| `CorrelationId`   | Correlation identifier copied from the context.             |
| `ResponsePayload` | Serialized consume outcome from `EventContext.Result`. |
| `RecentError` | Error recorded by the consume lifecycle observer. |
| `SourceType`      | CLR full name of the source business entity.                |
| `Source`          | Canonical name of the source entity.                        |
| `SourceTimestamp` | Concurrency token captured from the source at publish time. |

### EventState

```csharp
public enum EventState
{
    Recorded   = 0,
    Succeeded  = 1,
    Failed     = 2,
}
```

`Recorded` is written before dispatch or broker acceptance; it does not prove delivery. A consume
callback sets `Succeeded` or `Failed`. A consumer in another process can update the producer's row
only when its repository can find that row by `CorrelationId`. Broker confirmation alone leaves
the audit state `Recorded`.

## EventRouting

```csharp
public enum EventRouting
{
    Broadcast,           // every matched handler receives the event
    CompetingConsumers,  // exactly one matched handler receives the event
}
```

The default is `Broadcast`. Set per type with `EventBuilder.ConfigureRouting<TEvent>(routing)`,
stored in `IEventTypeRegistry` alongside the wire name, and read back through `GetRouting`.

## Extension points

- Implement `IEventPublishAdvisor` (`TryAddEnumerable`) for publish-time hooks: header injection,
  encryption, or short-circuit via `Block`/`Handle`.
- Implement `IEventConsumeAdvisor` (`TryAddEnumerable`) for consume-time hooks: metrics or
  dead-letter routing.
- Implement `IEventLifecycleObserver` (`TryAddEnumerable`) to observe publish, broker confirmation,
  and consume callbacks alongside the built-in audit observer.
- Implement `IEventBus` (scoped) to replace the transport.
- Durable subscriptions persist through `IRepository<SchemataEventSubscription>`; point the
  repository provider at a different store to relocate them.

## Caveats

- In-process handler failures propagate from `PublishAsync`. RabbitMQ publish failures also
  propagate, but a failure after broker acceptance can leave delivery uncertain. Caller-owned
  retries need idempotent consumers.
- Publishing after a business commit does not make the two operations atomic. A crash or publish
  failure can leave committed data without a delivered event.
- `RequireName(type)` throws for unregistered types. Register every event type used in
  `PublishAsync` during startup.
- The source-entity overload throws before publishing if the source does not implement both
  `ICanonicalName` and `IConcurrency`.
- `UseHandler` registrations accumulate (`TryAddEnumerable`): several `IEventHandler<TEvent>`
  implementations for one event all run (fan-out, or first-wins under `CompetingConsumers` routing).
- `SchemataEvent.EventType` stores the wire name. Queries against this column key on that string,
  not on a CLR type.

## See also

- [Dispatch Pipeline](dispatch-pipeline.md)
- [Providers](providers.md)
- [Scheduling Event Integration](../scheduling/event-integration.md)
