# Event Dispatch Pipeline

The dispatch pipeline runs on every `PublishAsync` and `SendAsync` call. It consists of two advisor stages — publish and consume — separated by the actual handler invocation. Advisors in each stage run sequentially in `Order` order and can short-circuit via `Block` or `Handle`. After the dispatch settles, every registered `IEventLifecycleObserver` is notified in a `finally` block.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Event.Skeleton` | `Advisors/IEventPublishAdvisor.cs`, `Advisors/IEventConsumeAdvisor.cs`, `IEventLifecycleObserver.cs`, `EventContext.cs`, `IEventDispatchContext.cs`, `EventRouting.cs` |
| `Schemata.Event.Foundation` | `Internal/InProcessEventBus.cs`, `Internal/HandlerResolver.cs`, `Internal/EventDispatchContext.cs`, `Observers/SchemataEventAuditObserver.cs` |

## Pipeline stages

```
PublishAsync<TEvent>(event)
    |
    v
IEventTypeRegistry.RequireName(typeof(TEvent))   -- throws if unregistered
    |
    v
EventContext created { Event, EventType (wire name), Payload (JSON), CorrelationId }
    |
    v
IEventPublishAdvisor pipeline (sorted by Order, application-level hooks)
    |-- custom advisors (Block throws, Handle returns cached result)
    |
    v (Continue) or short-circuit
    |
    v
IEventSubscriptionStore.FindAsync(eventType)     -- find subscriptions by wire name
    |
    v
HandlerResolver.InvokeEventHandlersAsync(event, routing)
    |-- Broadcast: all IEventHandler<TEvent> instances receive the event
    |-- CompetingConsumers: only one handler receives the event
    |
    v
IEventConsumeAdvisor pipeline (sorted by Order, runs in finally block)
    |-- custom advisors
    |
    v
IEventLifecycleObserver.OnPublishedAsync / OnConsumedAsync (runs in finally block)
    |-- SchemataEventAuditObserver  -- writes/updates the SchemataEvent audit row
    |-- custom observers
```

## IEventPublishAdvisor

```csharp
public interface IEventPublishAdvisor : IAdvisor<EventContext>;
```

Registered as scoped. Runs before handler invocation. The publish socket is left open for application pipelines; no built-in publish advisor ships in the box.

`Block` from a publish advisor throws `InvalidOperationException("Event publish blocked by advisor.")`. `Handle` with a value stashed in `AdviceContext` returns that value as the publish result without invoking handlers.

## IEventConsumeAdvisor

```csharp
public interface IEventConsumeAdvisor : IAdvisor<EventContext>;
```

Registered as scoped. Runs in a `finally` block after handler invocation, so it always executes regardless of whether the handler threw. `EventContext.Exception` is set before the consume pipeline runs if the handler threw; consume advisors can inspect it to publish to a dead-letter queue, emit metrics, or short-circuit retries.

## IEventLifecycleObserver

```csharp
public interface IEventLifecycleObserver
{
    Task OnPublishedAsync(EventContext context, CancellationToken ct);
    Task OnConsumedAsync(EventContext context, CancellationToken ct);
}
```

Registered through `TryAddEnumerable` as scoped. Every `IEventBus` implementation notifies observers in a `finally` block after publish/consume settles, regardless of advisor short-circuits or handler exceptions. Observer failures are swallowed and logged so they cannot mask handler exceptions or break a consumer ack loop.

The built-in `SchemataEventAuditObserver` writes a `SchemataEvent` row in `OnPublishedAsync` (`State = Recorded`) and updates the row in `OnConsumedAsync` (`State = Delivered` on success, `State = Failed` on exception). On cross-process consume the producer wrote the row in another process; the observer recovers it by `CorrelationId` and updates the existing row instead of creating a duplicate.

Implement and register additional observers to broadcast metrics, replicate to a downstream cache, or push to an outbox.

## HandlerResolver

`HandlerResolver` resolves `IEventHandler<TEvent>` instances from the DI scope and invokes them according to the routing policy:

- `Broadcast`: resolves all registered `IEventHandler<TEvent>` and calls each in sequence.
- `CompetingConsumers`: resolves the first registered `IEventHandler<TEvent>` and calls only that one.

For `SendAsync`, `HandlerResolver.InvokeRequestHandlerAsync<TRequest, TResponse>` resolves a single `IRequestHandler<TRequest, TResponse>`. Only one handler may be registered per request type; registering multiple handlers for the same request type results in the last registration winning (standard DI behavior).

## IEventDispatchContext

```csharp
public interface IEventDispatchContext
{
    IReadOnlyList<IEventSubscription>? MatchedSubscriptions { get; }
    void SetSubscriptions(IReadOnlyList<IEventSubscription>? subscriptions);
}
```

`EventDispatchContext` is registered as scoped by `UseConsumer(c => c.UseInProcess())`. It carries the subscription list for the current dispatch, allowing advisors and handlers to inspect which subscriptions matched.

## EventSubscription

```csharp
public sealed class EventSubscription : IEventSubscription
{
    public EventSubscription(string id, string eventType, string? correlationKey = null, string? target = null);
    public string  Id             { get; }
    public string  EventType      { get; }
    public string? CorrelationKey { get; }
    public string? Target         { get; }
}
```

`IEventSubscriptionStore.FindAsync(eventType, correlationKey)` returns subscriptions matching both the wire name and the optional correlation key. The Flow event integration uses `Target` to route events to specific process instances.

## Routing

`EventRouting` is configured per event type via `EventBuilder.ConfigureRouting<TEvent>(routing)`:

```csharp
schema.UseEvent()
      .RegisterEvent<OrderPlaced>("orders/order-placed")
      .ConfigureRouting<OrderPlaced>(EventRouting.CompetingConsumers);
```

Default is `Broadcast`.

## Extension points

- Implement `IEventPublishAdvisor` and register via `TryAddEnumerable` to add publish-time hooks.
- Implement `IEventConsumeAdvisor` and register via `TryAddEnumerable` to add consume-time hooks.
- Implement `IEventLifecycleObserver` and register via `TryAddEnumerable` to react to settled publish/consume events (audit, metrics, replication).
- Implement `IEventSubscriptionStore` to persist subscriptions in a database.
- Implement `IEventDispatchContext` to carry additional per-dispatch state.

## Design motivation

Audit is a side effect, not a request-shaping hook, so it moved from the advisor pipeline to a lifecycle observer. Running observers in a `finally` block ensures audit records are always updated even when a handler throws, and lets the observer surface swallow its own failures without disrupting the dispatch.

## Caveats

- The consume advisor pipeline runs even when the publish advisor short-circuits via `Handle`. In that case `EventContext.Result` is set but `EventContext.Exception` is null.
- `IRequestHandler<TRequest, TResponse>` is single-handler only. Registering multiple handlers for the same request type results in the last registration winning. This is a known limitation of the in-process bus.
- `IEventHandler<IEvent>` is a fallback path: if no more specific `IEventHandler<TEvent>` is registered, the fallback handler receives the event. The Flow event integration uses this path.

## See also

- [Overview](overview.md)
- [Providers](providers.md)
