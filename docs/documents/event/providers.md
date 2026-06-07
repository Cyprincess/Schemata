# Event Providers

Two transport backends ship with Schemata: an in-process bus for single-process deployments and testing, and a RabbitMQ bus for multi-process or distributed scenarios. Both enforce the `IEventTypeRegistry` wire-name contract.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Event.Foundation` | `Internal/InProcessEventBus.cs`, `Internal/InMemoryEventSubscriptionStore.cs`, `Builders/EventProducerBuilder.cs`, `Builders/EventConsumerBuilder.cs` |
| `Schemata.Event.RabbitMq` | `RabbitMqEventOptions.cs`, `Internal/RabbitMqEventBus.cs`, `Internal/RabbitMqConsumerHost.cs`, `Internal/CorrelationTracker.cs`, `Extensions/EventProducerBuilderRabbitMqExtensions.cs`, `Extensions/EventConsumerBuilderRabbitMqExtensions.cs` |

## In-process provider

The in-process provider is suitable for single-process applications and for testing. It resolves handlers from the DI container in a new scope per publish call.

### Registration

```csharp
schema.UseEvent()
      .RegisterEvent<OrderPlaced>("orders/order-placed")
      .UseProducer(p => p.UseInProcess())
      .UseConsumer(c => c.UseInProcess())
      .UseHandler<OrderPlaced, OrderPlacedHandler>();
```

`UseProducer(p => p.UseInProcess())` registers `InProcessEventBus` as `IEventBus` (scoped, `TryAdd`).

`UseConsumer(c => c.UseInProcess())` registers:
- `InMemoryEventSubscriptionStore` as `IEventSubscriptionStore` (singleton, `TryAdd`)
- `HandlerResolver` as scoped
- `EventDispatchContext` as `IEventDispatchContext` (scoped)

### InProcessEventBus behavior

1. Creates a new DI scope per `PublishAsync` call.
2. Calls `IEventTypeRegistry.RequireName(typeof(TEvent))` — throws for unregistered types.
3. Builds `EventContext` with the wire name, JSON payload, and a new correlation ID.
4. Runs the `IEventPublishAdvisor` pipeline.
5. Calls `IEventSubscriptionStore.FindAsync(wireName)` to find subscriptions.
6. Calls `HandlerResolver.InvokeEventHandlersAsync` according to the routing policy.
7. Runs the `IEventConsumeAdvisor` pipeline in a `finally` block.

### InMemoryEventSubscriptionStore

`InMemoryEventSubscriptionStore` is a thread-safe in-memory store. It does not survive application restarts. For production use with the Flow event integration, implement a persistent `IEventSubscriptionStore` backed by a database.

## RabbitMQ provider

The RabbitMQ provider bridges the event bus to a RabbitMQ broker using a topic exchange. It is configured via `RabbitMqEventOptions`.

### Registration

```csharp
schema.UseEvent()
      .RegisterEvent<OrderPlaced>("orders/order-placed")
      .UseProducer(p => p.UseRabbitMq())
      .UseConsumer(c => c.UseRabbitMq());
```

`UseRabbitMq()` on the producer builder registers `RabbitMqEventBus` as `IEventBus`.
`UseRabbitMq()` on the consumer builder registers `RabbitMqConsumerHost` as a hosted service.

### RabbitMqEventOptions

```csharp
public class RabbitMqEventOptions
{
    public string HostName              { get; set; } = "localhost";
    public int    Port                  { get; set; } = 5672;
    public string UserName              { get; set; } = "guest";
    public string Password              { get; set; } = "guest";
    public string VirtualHost           { get; set; } = "/";
    public string ExchangeName          { get; set; } = "schemata.events";
    public string ExchangeType          { get; set; } = "topic";
    public string QueueName             { get; set; } = "schemata.consumer";
    public int    ConnectionTimeoutMs   { get; set; } = 30000;
    public int    RequestTimeoutMs      { get; set; } = 30000;
    public ushort PrefetchCount         { get; set; } = 16;
    public string DeadLetterExchange    { get; set; } = "schemata.events.dlx";
    public string DeadLetterRoutingKey  { get; set; } = string.Empty;
}
```

Configure via `services.Configure<RabbitMqEventOptions>(...)` or `appsettings.json`.

### Routing key

The wire name registered via `RegisterEvent<TEvent>(name)` is used as the RabbitMQ routing key — `"orders/order-placed"` becomes the routing key on the topic exchange.

### Dead-letter exchange

When a handler throws, the message references an unregistered event type, or deserialization fails, the message is routed to `DeadLetterExchange`. Set `DeadLetterExchange = ""` to disable DLX routing (poison messages will be rejected without re-queue and lost).

### Request/reply

`SendAsync<TRequest, TResponse>` uses a correlation ID and a reply queue to implement request/reply over RabbitMQ. `CorrelationTracker` manages the pending reply futures.

## Caveats (RabbitMQ)

- `RabbitMqEventBus` is registered as **scoped** but owns a connection. The constructor uses synchronous waits on async connection open (`GetAwaiter().GetResult()`). This is a known limitation — avoid creating many short-lived scopes that each open a connection.
- `IRequestHandler<TRequest, TResponse>` is **single-handler only** on the RabbitMQ bus. Registering multiple handlers for the same request type results in only one receiving the message. This matches the in-process behavior but is worth noting explicitly for distributed deployments.
- `IEventHandler<IEvent>` is a **fallback path**. If no more specific `IEventHandler<TEvent>` is registered for a given wire name, the fallback handler receives the message. The Flow event integration uses this path.
- The RabbitMQ consumer host (`RabbitMqConsumerHost`) is a hosted service that starts consuming on application startup. Ensure the broker is reachable before the application starts, or implement a retry policy in the connection setup.

## Extension points

- Implement `IEventBus` and register as scoped to replace the transport entirely.
- Implement `IEventSubscriptionStore` to persist subscriptions in a database.
- Implement `IEventPublishAdvisor` or `IEventConsumeAdvisor` to add cross-cutting behavior (encryption, metrics, dead-letter routing) without modifying the transport.

## Design motivation

Separating producer and consumer registration (`UseProducer` / `UseConsumer`) allows applications that only publish events to avoid registering consumer infrastructure, and vice versa. This is useful for microservices where a service may be a pure producer or a pure consumer.

## See also

- [Overview](overview.md)
- [Dispatch Pipeline](dispatch-pipeline.md)
- [Cookbook: RabbitMQ Event Bus](../../cookbook/rabbitmq-event-bus.md)
