# Event Providers

Two transport backends ship: an in-process bus for single-process deployments and testing, and a
RabbitMQ bus for multi-process or distributed scenarios. Both enforce the `IEventTypeRegistry`
wire-name contract and both drive the same outbox dispatcher; they differ in how a published row
reaches handlers.

## Where the code lives

| Package                     | Key files                                                                                                                                                                                                                                                                                   |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Event.Foundation` | `Internal/InProcessEventBus.cs`, `Internal/InProcessEventOutboxPublisher.cs`, `SchemataEventSubscriptionExtensions.cs`, `EventOutboxDispatcher.cs`, `Builders/EventProducerBuilder.cs`, `Builders/EventConsumerBuilder.cs`                                                                  |
| `Schemata.Event.RabbitMq`   | `RabbitMqEventOptions.cs`, `Internal/RabbitMqEventBus.cs`, `Internal/RabbitMqConsumerHost.cs`, `Internal/RabbitMqEventOutboxPublisher.cs`, `Extensions/EventProducerBuilderRabbitMqExtensions.cs`, `Extensions/EventConsumerBuilderRabbitMqExtensions.cs` |
| `Schemata.Transport.RabbitMq` | `RabbitMqConnectionOptions.cs`, `IRabbitMqConnectionProvider.cs`, `CorrelationTracker.cs`, `Internal/RabbitMqConnectionProvider.cs`, `Extensions/ServiceCollectionExtensions.cs` |

## In-process provider

Suitable for single-process applications and tests. Handlers run on the same host that published,
driven by the outbox dispatcher rather than inline.

### Registration

```csharp
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

`InProcessEventBus.PublishAsync` records a `Pending` outbox row and returns (see
[Dispatch Pipeline](dispatch-pipeline.md)). The `EventOutboxDispatcher` claims the row and calls the
default `InProcessEventOutboxPublisher`, which deserializes the payload, loads subscriptions, runs
handlers, then the consume advisors and lifecycle observers. It returns `EventOutboxDelivery.Consumed`
because the consume path already owns the terminal `Succeeded`/`Failed` state.

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
schema.UseEvent()
      .RegisterEvent<OrderPlaced>("orders/order-placed")
      .UseProducer(p => p.UseRabbitMq())
      .UseConsumer(c => c.UseRabbitMq());
```

`UseRabbitMq()` on the producer registers `RabbitMqEventBus` as a scoped `IEventBus` and
`RabbitMqEventOutboxPublisher` as the `IEventOutboxPublisher` singleton. On the consumer it registers
`RabbitMqConsumerHost` as a hosted service. Both call `AddRabbitMqTransport()`, which contributes the
shared `IRabbitMqConnectionProvider` and `CorrelationTracker` singletons; calling it from both sides
is idempotent, which is what keeps one connection per process.

### Connection lifecycle

The broker connection belongs to `IRabbitMqConnectionProvider` in `Schemata.Transport.RabbitMq`, and
every client in the process shares that one `IConnection`. It is not opened in any constructor: the
provider connects lazily on the first `GetConnectionAsync` call, guarded by a `SemaphoreSlim(1, 1)`
so concurrent first callers share one connection attempt. A failed attempt leaves the field null so
the next call retries cleanly. A host with an unreachable broker starts normally; only the first
publish or consume observes the failure.

Channels remain per-client and are disposed by their owner: the bus keeps its reply channel, the
consumer host its consume channel, and the outbox publisher opens a publisher-confirm channel per
publish. None of them closes the shared connection.

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

### Outbox and confirms

`RabbitMqEventBus.PublishAsync` records the `Pending` outbox row through the audit observer, exactly
as the in-process bus does. The `EventOutboxDispatcher` replays the row through
`RabbitMqEventOutboxPublisher`, which opens a publisher-confirm channel
(`CreateChannelOptions(publisherConfirmations: true, publisherConfirmationTracking: true)`),
publishes with `DeliveryModes.Persistent`, and completes only on broker confirmation. It returns
`EventOutboxDelivery.Delivered`, so a downstream `RabbitMqConsumerHost` records the terminal state on
consume.

### Dead-letter exchange

`RabbitMqConsumerHost` declares the queue with `x-dead-letter-exchange` set to `DeadLetterExchange`
(default `schemata.events.dlx`, a topic exchange). A message is rejected with `BasicNackAsync(requeue: false)`
— and so dead-lettered — when the handler throws, the routing key resolves to an unregistered type,
or deserialization returns null. Setting `DeadLetterExchange = string.Empty` skips the DLX
declaration; poison messages are then rejected without requeue and dropped.

### Backpressure

`PrefetchCount` bounds the unacknowledged window per consumer (`BasicQosAsync`), so a slow handler
stops the broker from sending more work rather than starving other consumers.

### Request/reply lives elsewhere

The event bus is broadcast-only. Cross-process request/reply is `Schemata.Messaging.RabbitMq`, which
registers its own `IRequestDispatcher` through `AddRabbitMqRequestDispatcher(...)` and owns its reply
queue and correlation handling. Both packages share the one `IConnection` and the
`CorrelationTracker` from `Schemata.Transport.RabbitMq`. See [Messaging](../messaging/overview.md).

## Caveats (RabbitMQ)

- `RabbitMqEventBus` is registered scoped but opens a broker connection in its constructor using
  synchronous waits on async connection open (`GetAwaiter().GetResult()`). Inject `IEventBus` into
  long-lived services; many short-lived scopes each open a connection.
- `IEventHandler<IEvent>` is a fallback path: with no more specific handler for a wire name, the
  fallback handler receives the message.

## Extension points

- Implement `IEventBus` (scoped) to replace the transport.
- Implement `IEventOutboxPublisher` (singleton) to replay outbox rows over a custom broker.
- Implement `IEventPublishAdvisor` or `IEventConsumeAdvisor` to add cross-cutting behavior without
  touching the transport.

## See also

- [Overview](overview.md)
- [Dispatch Pipeline](dispatch-pipeline.md)
- [Cookbook: RabbitMQ Event Bus](../../cookbook/rabbitmq-event-bus.md)
