# Event Dispatch Pipeline

The bus separates publishing from handler invocation through a transactional outbox. `PublishAsync`
runs the publish-side advisor pipeline, records an audit row, and returns; it does not call
handlers. The `EventOutboxDispatcher` background service later claims that row and replays it through
the handler resolver, the consume-side advisor pipeline, and the lifecycle observers.
`SendAsync` is the exception: request/reply runs its single handler inline because the caller awaits
the response.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Event.Skeleton` | `Advisors/IEventPublishAdvisor.cs`, `Advisors/IEventConsumeAdvisor.cs`, `IEventLifecycleObserver.cs`, `IEventOutboxPublisher.cs`, `EventOutboxMessage.cs`, `EventOutboxDelivery.cs`, `EventContext.cs`, `IEventDispatchContext.cs`, `EventRouting.cs` |
| `Schemata.Event.Foundation` | `Internal/InProcessEventBus.cs`, `Internal/InProcessEventOutboxPublisher.cs`, `EventOutboxDispatcher.cs`, `Internal/HandlerResolver.cs`, `Observers/SchemataEventAuditObserver.cs` |

## Publish path

`PublishAsync<TEvent>` (in `InProcessEventBus` and `RabbitMqEventBus`):

1. `IEventTypeRegistry.RequireName(@event.GetType())` — throws if unregistered.
2. Builds an `EventContext` with the wire name, JSON payload, a fresh correlation id,
   `RequiresOutboxDelivery = true`, and the optional source entity.
3. Runs the `IEventPublishAdvisor` pipeline (sorted by `Order`).
   - `Continue` proceeds; `Block` throws `InvalidOperationException("Event publish blocked by advisor.")`;
     `Handle` with a value stashed in `AdviceContext` sets `Result` and returns without recording.
4. Notifies every `IEventLifecycleObserver.OnPublishedAsync`. The audit observer writes a
   `SchemataEvent` row in state `Pending` (because `RequiresOutboxDelivery` is set).
5. Wakes the `EventOutboxDispatcher` via `NotifyPending()` and returns. No handler has run.

## Outbox dispatch

`EventOutboxDispatcher` is a `BackgroundService` that wakes on `NotifyPending()` or every 30 seconds.
Each pass claims rows in state `Pending`, plus any `Publishing` row a crashed dispatcher left stale
past a five-minute claim timeout (batch size 100). For each row:

1. Transitions the row to `Publishing` under its concurrency token; a `AbortedException` means a
   competing dispatcher won the claim, so the row is skipped.
2. Calls `IEventOutboxPublisher.PublishAsync(EventOutboxMessage)`, passing the persisted payload,
   correlation id, and source snapshot.
3. Inspects the returned `EventOutboxDelivery`:
   - `Delivered` — the broker accepted the message; a downstream consumer sets the terminal state.
     The dispatcher marks the row `Recorded` as a fallback.
   - `Consumed` — the publisher already replayed handlers in-process and the consume path owns the
     terminal `Succeeded`/`Failed` state; the dispatcher leaves it untouched.
4. On a publish failure, returns the row to `Pending`, increments `RetryCount`, records the error,
   and the next pass retries. Delivery is at-least-once.

The default `InProcessEventOutboxPublisher` returns `Consumed`; `RabbitMqEventOutboxPublisher`
returns `Delivered`.

## Consume path

The in-process publisher (`InProcessEventOutboxPublisher.PublishAsync`) and the RabbitMQ consumer
(`RabbitMqConsumerHost`) share the same consume shape:

1. Resolve the wire name back to a CLR type via `IEventTypeRegistry.Resolve`. An unknown name is a
   poison message (dropped in-process, dead-lettered on RabbitMQ).
2. Deserialize the payload and load matching subscriptions through
   `IRepository<SchemataEventSubscription>.ListMatchingAsync`, exposing them on
   `IEventDispatchContext`.
3. Invoke handlers through `HandlerResolver.InvokeEventHandlersAsync` under the type's
   `EventRouting`.
4. In a `finally` block, run the `IEventConsumeAdvisor` pipeline, then notify every
   `IEventLifecycleObserver.OnConsumedAsync`.

`EventContext.Result` (or `Exception`) reflects the handler outcome before the consume pipeline runs.

## IEventPublishAdvisor

```csharp
public interface IEventPublishAdvisor : IAdvisor<EventContext>;
```

Runs before the row is recorded. `Block` throws; `Handle` returns a cached result without recording
an outbox row. No built-in publish advisor ships.

## IEventConsumeAdvisor

```csharp
public interface IEventConsumeAdvisor : IAdvisor<EventContext>;
```

Runs in the `finally` block after handler invocation, so it always executes whether the handler
returned or threw. Inspect `EventContext.Exception` to route to a dead-letter queue, emit metrics,
or count retries. The bus consumes all three `AdviseResult` values identically here — the advisor is
observational at this stage.

## IEventLifecycleObserver

```csharp
public interface IEventLifecycleObserver
{
    Task OnPublishedAsync(EventContext context, CancellationToken ct = default);
    Task OnDeliveredAsync(EventContext context, CancellationToken ct = default);  // default no-op
    Task OnConsumedAsync(EventContext context, CancellationToken ct = default);
}
```

Registered through `TryAddEnumerable` as scoped. `OnPublishedAsync` fires after the publish advisor
returns `Continue`; `OnDeliveredAsync` fires after a durable broker confirms a publish (the outbox
path); `OnConsumedAsync` fires after handler dispatch settles. Observer failures in the consume
notification are logged at `Warning` and swallowed so they cannot mask a handler exception.

The built-in `SchemataEventAuditObserver`:

- `OnPublishedAsync` — writes the `SchemataEvent` row as `Pending` when `RequiresOutboxDelivery` is
  set, otherwise `Recorded`; captures the source snapshot.
- `OnDeliveredAsync` — transitions the row to `Recorded`, recovering it by `CorrelationId` if the
  context lost its `Record` reference (cross-process delivery).
- `OnConsumedAsync` — sets `Succeeded` (with the serialized response) or `Failed` (with the error),
  recovering the producer's row by `CorrelationId` on cross-process consume.

## HandlerResolver

`HandlerResolver` resolves handlers from the DI scope and invokes them per `EventRouting`:

- `Broadcast` — resolves every `IEventHandler<TEvent>` and awaits them all.
- `CompetingConsumers` — invokes only the first registered `IEventHandler<TEvent>`.

When no `IEventHandler<TEvent>` is registered, the resolver falls back to `IEventHandler<IEvent>`
instances; with neither, it throws `InvalidOperationException`. For `SendAsync`,
`InvokeRequestHandlerAsync<TRequest, TResponse>` requires exactly one
`IRequestHandler<TRequest, TResponse>` and throws if zero or more than one are registered.

## IEventDispatchContext

```csharp
public interface IEventDispatchContext
{
    IReadOnlyList<SchemataEventSubscription>? MatchedSubscriptions { get; }
    void SetSubscriptions(IReadOnlyList<SchemataEventSubscription>? subscriptions);
}
```

`EventDispatchContext` is registered as scoped by `UseConsumer(c => c.UseInProcess())`. The bus sets
the matched subscriptions before handler invocation; handlers and advisors read them.

## SchemataEventSubscription

Subscriptions are persisted entities in `Schemata.Event.Skeleton.Entities`:

```csharp
public class SchemataEventSubscription : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    public string  EventType      { get; set; }
    public string? CorrelationKey { get; set; }
    public string  Target         { get; set; }
    public string  SubscriptionId { get; set; }
    // plus framework traits (Uid, Name, CanonicalName, Timestamp, CreateTime, UpdateTime)
}
```

`IRepository<SchemataEventSubscription>.ListMatchingAsync(eventType, correlationKey)` returns
subscriptions matching the wire name and the optional correlation key. The extension lives in
`Schemata.Event.Foundation.SchemataEventSubscriptionExtensions`.

## Routing

`EventRouting` is configured per event type via `EventBuilder.ConfigureRouting<TEvent>(routing)`:

```csharp
schema.UseEvent()
      .RegisterEvent<OrderPlaced>("orders/order-placed")
      .ConfigureRouting<OrderPlaced>(EventRouting.CompetingConsumers);
```

The default is `Broadcast`.

## Caveats

- `PublishAsync` returns before any handler runs. Handler effects are observable only after the
  outbox dispatcher drains the row.
- The consume advisor pipeline runs even when the publish advisor short-circuits via `Handle` on the
  `SendAsync` path; `EventContext.Result` is set and `Exception` is null.
- `IRequestHandler<TRequest, TResponse>` is single-handler only. Two registrations for the same
  request type throw at dispatch.
- `IEventHandler<IEvent>` is a fallback: it receives any event with no more specific handler
  registered.

## See also

- [Overview](overview.md)
- [Providers](providers.md)
