# Event Dispatch Pipeline

`PublishAsync` runs the publish-side advisor pipeline and lifecycle observers before dispatch.
`InProcessEventBus` then awaits handlers inline. `RabbitMqEventBus` publishes directly to the broker
and awaits publisher confirmation; `RabbitMqConsumerHost` invokes handlers separately. Audit rows
record these lifecycle callbacks. They are not a transactional outbox or a publish-retry queue.

The bus is broadcast-only. Request/reply is a different shape — one handler, one answer — and lives
on `IRequestDispatcher` in [Messaging](../messaging/overview.md).

## Where the code lives

| Package                     | Key files                                                                                                                                                                                                                                             |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Event.Skeleton` | `Advisors/IEventPublishAdvisor.cs`, `Advisors/IEventConsumeAdvisor.cs`, `IEventLifecycleObserver.cs`, `EventContext.cs`, `IEventDispatchContext.cs`, `EventRouting.cs` |
| `Schemata.Event.Foundation` | `Runtime/InProcessEventBus.cs`, `Runtime/HandlerResolver.cs`, `Observers/SchemataEventAuditObserver.cs` |
| `Schemata.Event.RabbitMq` | `Runtime/RabbitMqEventBus.cs`, `Runtime/RabbitMqConsumerHost.cs` |

## Publish path

`PublishAsync<TEvent>` (in `InProcessEventBus` and `RabbitMqEventBus`):

1. `IEventTypeRegistry.RequireName(@event.GetType())` — throws if unregistered.
2. Builds an `EventContext` with the wire name, JSON payload, a fresh correlation id,
   and the optional source entity, within a new DI scope and `AdviceContext`.
3. Runs the `IEventPublishAdvisor` pipeline (sorted by `Order`).
   - `Continue` proceeds; `Block` throws `InvalidOperationException("Event publish blocked by advisor.")`;
     `Handle` with a value stashed in `AdviceContext` sets `Result` and returns without recording.
4. Notifies `IEventLifecycleObserver.OnPublishedAsync`. The audit observer adds and commits a
   `SchemataEvent` row in state `Recorded`; audit persistence needs the naming configuration
   described in [Audit row names](#audit-row-names). The in-process bus logs observer failures and
   continues; the RabbitMQ bus propagates them before contacting the broker.
5. The in-process bus loads subscriptions and invokes registered handlers with the original event
   instance. If neither typed nor fallback handlers exist, it returns successfully without consume
   callbacks. The RabbitMQ bus opens a publisher-confirm channel, declares the durable exchange,
   and awaits a mandatory publish with `DeliveryModes.Persistent`.
6. RabbitMQ calls `OnDeliveredAsync` after confirmation. In-process dispatch runs consume advisors
   and observers before returning. Neither provider automatically retries a failed publish.

## Audit row names

The `SchemataEvent` row written by `SchemataEventAuditObserver` carries a canonical name
(`CanonicalName`) derived from the entity's `[CanonicalName("events/{event}")]` pattern
(see `Schemata.Event.Skeleton.Entities.SchemataEvent`). The leaf placeholder `{event}` binds to
`ICanonicalName.Name` via `ResourceNameDescriptor`, so `CanonicalName` resolves to
`events/{Name}` at add time. `Name` itself is not set by the observer; the observer copies the
wire event type into `EventType` and leaves `Name` to the add pipeline.

An application using `Schemata.Event.Foundation` audit persistence must therefore register an
`IRepositoryAddAdvisor<SchemataEvent>` that copies `EventType` into `Name` before the canonical
resolve runs. The relevant prefix of the built-in add-advisor chain is:

1. `AdviceAddIdentifier<SchemataEvent>` at order `90_000_000` assigns `Uid`.
2. `AdviceAddTimestamp<SchemataEvent>` at order `100_000_000` assigns creation and update times.
3. `AdviceAddConcurrency<SchemataEvent>` at order `110_000_000` assigns the concurrency token.
4. `AdviceAddCanonicalName<SchemataEvent>` at order `120_000_000` resolves `CanonicalName`.

The application Name advisor runs at order `50_000_000`, before this built-in prefix.
`AdviceAddCanonicalName<TEntity>` delegates to `ResourceNameDescriptor.Resolve`, which throws
`ValidationException` when a required placeholder value is empty. With no Name advisor, `Name`
is `null`, `Resolve` throws on `events/{event}`, and the audit observer's `AddAsync` call
never commits a row.

`InProcessEventBus.NotifyPublishedAsync` (in `Schemata.Event.Foundation.Runtime`) catches audit
observer failures, logs `IEventLifecycleObserver.OnPublishedAsync threw for event '{EventType}'.`
at `Warning`, and continues toward handler dispatch. Missing audit persistence therefore does not
by itself prevent in-process delivery. `RabbitMqEventBus` propagates the same observer failure and
does not publish to the broker. Configure audit naming and persistence on both producer types.

## Delivery and transaction boundaries

`PublishAsync` performs delivery work within the call. A RabbitMQ confirmation records broker
acceptance, not consumer success or a commit of the application's business transaction. An
`OnDeliveredAsync` failure can fault the call after the broker has already accepted the message.

`SchemataEventAuditObserver` commits its audit row before delivery and has no replay loop. A
`Recorded` row can remain after a failed publish, a publish with no in-process handlers, or a
RabbitMQ consume whose repository cannot find the producer's row. The audit observer's inherited
`OnDeliveredAsync` is a no-op; broker confirmation does not change the audit state.

Publishing from a committed repository advisor protects against publishing a rolled-back mutation,
but a crash between business commit and publish can still lose the event. Applications own any
atomic delivery mechanism and retry policy they require.

## Consume path

The in-process bus loads matching subscriptions, probes for handlers, and dispatches the original
event instance. An empty subscription list does not suppress registered in-process handlers.

`RabbitMqConsumerHost` resolves the routing key through `IEventTypeRegistry.Resolve`, loads matching
subscriptions, and deserializes the payload. An unknown wire name is rejected without requeue. A
known event with no matching persisted subscriptions is acknowledged and dropped before handler
dispatch. With subscriptions present, a null or invalid payload or a missing handler fails delivery.

Both paths invoke `HandlerResolver.InvokeEventHandlersAsync` under the event's `EventRouting`, set
`EventContext.Result = true` on success, and capture handler failures in `EventContext.Exception`.
They run consume advisors after handler invocation, including handler failures. RabbitMQ does this
in a `finally` block; in-process dispatch captures the exception and runs the consume path before
rethrowing. An advisor exception exits before consume observers run.

Consume observers run with the audit observer last. The first observer failure is captured in
`EventContext.Exception`; remaining observers still run, then the failure propagates. In-process
failures reach the publish caller. RabbitMQ acknowledges successful handling and rejects failures
with `BasicNackAsync(requeue: false)` for the configured dead-letter routing. It logs acknowledgment
transport failures separately.

## IEventPublishAdvisor

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Event.Skeleton;

public interface IEventPublishAdvisor : IAdvisor<EventContext>;
```

Runs before lifecycle observers and delivery. `Block` throws; `Handle` with a value in
`AdviceContext` returns without recording or dispatching. A `Handle` without a value also throws.
Applications register their own publish advisors.

## IEventConsumeAdvisor

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Event.Skeleton;

public interface IEventConsumeAdvisor : IAdvisor<EventContext>;
```

Runs after handler invocation, including captured handler failures. Inspect `EventContext.Exception`
to emit metrics or implement application-owned failure handling. The bus ignores the returned
`AdviseResult`; an exception still propagates and prevents the subsequent observer callbacks.

## IEventLifecycleObserver

```csharp
using System.Threading;
using System.Threading.Tasks;
using Schemata.Event.Skeleton;

public interface IEventLifecycleObserver
{
    Task OnPublishedAsync(EventContext context, CancellationToken ct = default);
    Task OnDeliveredAsync(EventContext context, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnConsumedAsync(EventContext context, CancellationToken ct = default);
}
```

Registered through `TryAddEnumerable` as scoped. `OnPublishedAsync` runs after publish advisors
continue; `OnDeliveredAsync` runs only in the RabbitMQ producer after confirmation;
`OnConsumedAsync` runs after the consume advisors.

Failure handling differs by callback:

- `OnPublishedAsync`: the in-process bus logs each failure at `Warning` and continues. The RabbitMQ
  bus propagates the first failure before publishing.
- `OnDeliveredAsync`: RabbitMQ propagates a failure after broker acceptance. Subsequent observers
  do not run and the bus does not retry. In-process publishing does not call this callback.
- `OnConsumedAsync`: the first failure is captured while remaining observers run, with the audit
  observer last. The failure then reaches the in-process caller or RabbitMQ's rejection path.

The built-in `SchemataEventAuditObserver`:

- `OnPublishedAsync` adds and commits a `Recorded` row with the source snapshot.
- `OnDeliveredAsync` uses the interface's default no-op implementation.
- `OnConsumedAsync` sets `Succeeded` with the serialized result or `Failed` with the error. If the
  context lacks a record, it queries by `CorrelationId`; it returns without writing when no row
  is visible. Cross-process audit updates therefore require access to the producer's audit store.

## HandlerResolver

`HandlerResolver` resolves handlers from the DI scope and invokes them per `EventRouting`:

- `Broadcast` — resolves every `IEventHandler<TEvent>` and awaits them all.
- `CompetingConsumers` — invokes only the first registered `IEventHandler<TEvent>`.

When no `IEventHandler<TEvent>` is registered, the resolver falls back to `IEventHandler<IEvent>`
instances; with neither, invocation throws `InvalidOperationException`. The in-process publish
path probes with `HasHandlers(Type)` first and treats zero handlers as a successful broadcast.

The resolver handles events only. `IRequest<TResponse>` and `IRequestHandler<TRequest, TResponse>`
belong to `Schemata.Messaging.Skeleton`, and their dispatch belongs to `IRequestDispatcher` — see
[Messaging](../messaging/overview.md).

## IEventDispatchContext

```csharp
using System.Collections.Generic;
using Schemata.Event.Skeleton.Entities;

public interface IEventDispatchContext
{
    IReadOnlyList<SchemataEventSubscription>? MatchedSubscriptions { get; }
    void SetSubscriptions(IReadOnlyList<SchemataEventSubscription>? subscriptions);
}
```

`EventDispatchContext` is registered as scoped by `UseConsumer(c => c.UseInProcess())`. The bus sets
the matched subscriptions before handler invocation; handlers and advisors read them.

## SchemataEventSubscription

Selected subscription fields are listed below; the full entity and framework traits are declared in
`src/Schemata.Event.Skeleton/Entities/SchemataEventSubscription.cs`.

| Field | Type |
| --- | --- |
| `EventType` | `string` |
| `CorrelationKey` | `string?` |
| `Target` | `string` |
| `Token` | `string?` |
| `SubscriptionId` | `string` |

`IRepository<SchemataEventSubscription>.ListMatchingAsync(eventType, correlationKey)` returns
subscriptions matching the wire name and the optional correlation key. The extension lives in
`Schemata.Event.Foundation.SchemataEventSubscriptionExtensions`.

## Routing

`EventRouting` is configured per event type via `EventBuilder.ConfigureRouting<TEvent>(routing)`:

```csharp
using Microsoft.AspNetCore.Builder;
using Schemata.Event.Skeleton;

schema.UseEvent()
      .RegisterEvent<OrderPlaced>("orders/order-placed")
      .ConfigureRouting<OrderPlaced>(EventRouting.CompetingConsumers);
```

The default is `Broadcast`.

## Caveats

- In-process `PublishAsync` awaits handlers; RabbitMQ `PublishAsync` awaits broker confirmation,
  not consumer completion. Audit persistence does not provide atomic delivery or automatic retries.
- `IEventHandler<IEvent>` is a fallback: it receives any event with no more specific handler
  registered.
- A RabbitMQ `OnDeliveredAsync` failure can reach the caller after broker acceptance. Consume
  observer failures propagate; in-process `OnPublishedAsync` failures are isolated at `Warning`.

## See also

- [Overview](overview.md)
- [Providers](providers.md)
