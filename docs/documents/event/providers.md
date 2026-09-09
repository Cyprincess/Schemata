# Event Providers

Two transport backends ship: an in-process bus for single-process deployments and testing, and a
RabbitMQ bus for multi-process scenarios. Both enforce the `IEventTypeRegistry` wire-name contract.
The in-process bus awaits handlers; the RabbitMQ bus awaits publisher confirmation.

## Where the code lives

| Package                     | Key files                                                                                                                                                                                                                                                                                   |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Event.Foundation` | `Runtime/InProcessEventBus.cs`, `Runtime/HandlerResolver.cs`, `SchemataEventSubscriptionExtensions.cs`, `Builders/EventProducerBuilder.cs`, `Builders/EventConsumerBuilder.cs` |
| `Schemata.Event.RabbitMq` | `RabbitMqEventOptions.cs`, `Runtime/RabbitMqEventBus.cs`, `Runtime/RabbitMqConsumerHost.cs`, `Extensions/EventProducerBuilderRabbitMqExtensions.cs`, `Extensions/EventConsumerBuilderRabbitMqExtensions.cs` |
| `Schemata.Transport.RabbitMq` | `RabbitMqConnectionOptions.cs`, `IRabbitMqConnectionProvider.cs`, `CorrelationTracker.cs`, `Runtime/RabbitMqConnectionProvider.cs`, `Extensions/ServiceCollectionExtensions.cs` |

## In-process provider

Suitable for single-process applications and tests. Handlers run on the publishing host within
`PublishAsync`, which awaits their completion.

### Registration

```csharp
using Microsoft.AspNetCore.Builder;

schema.UseEvent()
      .RegisterEvent<OrderPlaced>("orders/order-placed")
      .UseProducer(p => p.UseInProcess())
      .UseConsumer(c => c.UseInProcess())
      .UseHandler<OrderPlaced, OrderPlacedHandler>();
```

`UseProducer(p => p.UseInProcess())` registers `InProcessEventBus` as a scoped `IEventBus`
(`TryAdd`).

`UseConsumer(c => c.UseInProcess())` registers, all scoped (`TryAdd`):

- `HandlerResolver`
- `EventDispatchContext` as `IEventDispatchContext`

Subscriptions are persisted through `IRepository<SchemataEventSubscription>`, which a persistence
provider (EF Core or LinqToDB) must register for the in-process consumer to resolve.

### Behavior

`InProcessEventBus.PublishAsync` runs publish advisors and observers, loads subscriptions, and
passes the original event instance to registered handlers. It then runs consume advisors and
observers before returning or propagating a failure. With no typed or fallback handlers, it returns
successfully without consume callbacks. A persisted subscription is not required for an in-process
handler to run. See [Dispatch Pipeline](dispatch-pipeline.md).

### Subscription persistence

`SchemataEventSubscription` rows persist through `IRepository<SchemataEventSubscription>`, so
subscriptions survive restarts. `SchemataEventSubscriptionExtensions.ListMatchingAsync(eventType,
correlationKey)` is the read-side helper the in-process publisher and the RabbitMQ consumer use to
resolve matching subscriptions during dispatch.

## RabbitMQ provider

Bridges the bus to a RabbitMQ broker over a topic exchange. Topology is configured via
`RabbitMqEventOptions`; the broker connection itself is configured via `RabbitMqConnectionOptions`
and owned by `Schemata.Transport.RabbitMq`.

### Registration

```csharp
using Microsoft.AspNetCore.Builder;

schema.UseEvent()
      .RegisterEvent<OrderPlaced>("orders/order-placed")
      .UseProducer(p => p.UseRabbitMq())
      .UseConsumer(c => c.UseInProcess().UseRabbitMq());
```

`UseRabbitMq()` on the producer registers `RabbitMqEventBus` as a scoped `IEventBus`. On the
consumer it registers `RabbitMqConsumerHost` as a hosted service. `UseInProcess()` supplies that
host's scoped handler resolver and dispatch context. Both RabbitMQ extensions call
`AddRabbitMqTransport()`, which uses `TryAddSingleton` for the shared connection provider and
`CorrelationTracker`.

Configure repositories for `SchemataEvent` and `SchemataEventSubscription`, audit naming as described
in [Dispatch Pipeline](dispatch-pipeline.md#audit-row-names), and matching persisted subscriptions.
The RabbitMQ consumer acknowledges and drops a registered event with no matching subscription;
`UseHandler` alone does not create a subscription row.

### Connection lifecycle

The broker connection belongs to `IRabbitMqConnectionProvider` in `Schemata.Transport.RabbitMq`, and
every client in the process shares that one `IConnection`. It is not opened in any constructor: the
provider connects lazily on the first `GetConnectionAsync` call, guarded by a `SemaphoreSlim(1, 1)`
so concurrent first callers share one connection attempt. A failed attempt leaves the field null so
the next connection request can attempt initialization again. A producer-only host connects on
publish; a consumer host requests the connection when its background service starts, so broker
unavailability can fail that service.

Each publish opens and disposes a publisher-confirm channel. The consumer host owns its consume
channel. The event bus has no reply channel, and neither client closes the shared connection.

### RabbitMqEventOptions

```csharp
public class RabbitMqEventOptions
{
    public string ExchangeName         { get; set; } = "schemata.events";
    public string ExchangeType         { get; set; } = "topic";
    public string QueueName            { get; set; } = "schemata.consumer";
    public int    RequestTimeoutMs     { get; set; } = 30000;
    public ushort PrefetchCount        { get; set; } = 16;
    public string DeadLetterExchange   { get; set; } = "schemata.events.dlx";
    public string DeadLetterRoutingKey { get; set; } = string.Empty;
}
```

Configure via the `UseRabbitMq(o => ...)` delegate, `services.Configure<RabbitMqEventOptions>(...)`,
or `appsettings.json`.

### RabbitMqConnectionOptions

Declared in `Schemata.Transport.RabbitMq` and shared by every RabbitMQ client in the process.

```csharp
public class RabbitMqConnectionOptions
{
    public string HostName            { get; set; } = "localhost";
    public int    Port                { get; set; } = 5672;
    public string UserName            { get; set; } = "guest";
    public string Password            { get; set; } = "guest";
    public string VirtualHost         { get; set; } = "/";
    public int    ConnectionTimeoutMs { get; set; } = 30000;
}
```

Configure via the second `UseRabbitMq(null, c => ...)` delegate, a direct
`services.AddRabbitMqTransport(c => ...)`, `services.Configure<RabbitMqConnectionOptions>(...)`, or
`appsettings.json`.

### Routing key

The wire name is the RabbitMQ routing key. `"orders/order-placed"` becomes the routing key on the
topic exchange. `RabbitMqConsumerHost` binds the queue with `#`, receiving every routing key and
resolving each back to a CLR type through the registry.

### Publisher confirmation and audit records

`RabbitMqEventBus.PublishAsync` runs publish advisors and observers, then opens a channel with
publisher confirmations and confirmation tracking enabled. It declares the durable exchange and
awaits `BasicPublishAsync` with a persistent message and `mandatory: true`, then calls
`OnDeliveredAsync` observers. The call waits for broker acceptance, not consumer success.

The audit observer commits a `Recorded` row before the broker publish and does not change its state
on confirmation. A consumer updates that row only if its repository can find it by `CorrelationId`.
The row is an audit record, not a transactional outbox; the bus does not automatically retry a
failed publish. Business commits, audit commits, and broker acceptance are separate operations.

### Dead-letter exchange

`RabbitMqConsumerHost` declares the queue with `x-dead-letter-exchange` set to `DeadLetterExchange`
(default `schemata.events.dlx`, declared as a topic exchange). It acknowledges registered events
with no matching subscriptions. With subscriptions present, handler, consume-advisor, observer, or
deserialization failures are rejected with `BasicNackAsync(requeue: false)`. Unknown routing keys
are also rejected. Rejected messages use the configured dead-letter routing; a bound dead-letter
queue is needed to retain them. Setting `DeadLetterExchange = string.Empty` skips this declaration
and queue argument, so rejected messages are discarded unless broker policy supplies a DLX.

### Backpressure

`PrefetchCount` bounds the unacknowledged window per consumer (`BasicQosAsync`), so a slow handler
stops the broker from sending more work rather than starving other consumers.

### Request/reply lives elsewhere

The event bus is broadcast-only. Cross-process request/reply is `Schemata.Messaging.RabbitMq`, which
registers its own `IRequestDispatcher` through `AddRabbitMqRequestDispatcher(...)` and owns its reply
queue and correlation handling. Both packages use the shared connection provider; request/reply
uses `CorrelationTracker` from `Schemata.Transport.RabbitMq`. See [Messaging](../messaging/overview.md).

## Caveats (RabbitMQ)

- `IEventBus` is scoped; the broker connection provider is shared. A publish opens a channel, not a
  separate connection per scope. A failed post-confirm observer can fault `PublishAsync` after the
  broker accepted the message, so caller-owned retries need idempotent consumers.
- `IEventHandler<IEvent>` is a fallback path: with no more specific handler for a wire name, the
  fallback handler receives the message.

## Extension points

- Implement `IEventBus` (scoped) to replace the transport.
- Implement `IEventPublishAdvisor` or `IEventConsumeAdvisor` to add cross-cutting behavior without
  touching the transport.

## See also

- [Overview](overview.md)
- [Dispatch Pipeline](dispatch-pipeline.md)
- [Cookbook: RabbitMQ Event Bus](../../cookbook/rabbitmq-event-bus.md)
