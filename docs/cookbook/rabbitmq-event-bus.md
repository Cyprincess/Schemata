# RabbitMQ Event Bus

## What you'll build

A Schemata application that publishes an `OrderPlaced` event to RabbitMQ, consumes it in a handler,
dead-letters poison messages, and performs a synchronous request/reply over the same broker. By the
end you'll have a producer, a consumer host, DLX topology, and a typed `SendAsync` call.

## Prerequisites

- A running RabbitMQ broker (default `localhost:5672`, credentials `guest/guest`).
- The `Schemata.Event.RabbitMq` package added to your project.
- A persistence provider (EF Core or LinqToDB) so the outbox audit rows can be stored.
- Familiarity with the in-process bus from [guides/event-bus.md](../guides/event-bus.md).

## Step 1: Define the event and request types

```csharp
using Schemata.Event.Skeleton;

public sealed class OrderPlaced : IEvent
{
    public string  OrderId { get; init; } = string.Empty;
    public decimal Total   { get; init; }
}

// For request/reply
public sealed class PriceQuery : IRequest<PriceResult>
{
    public string ProductId { get; init; } = string.Empty;
}

public sealed class PriceResult
{
    public decimal Price { get; init; }
}
```

Fire-and-forget types implement `IEvent`; request/reply types implement `IRequest<TResponse>`. The
CLR type name is never the routing key; assign the wire name in Step 2.

**Assertion:** the project compiles with no errors referencing `IEvent` or `IRequest<>`.

## Step 2: Register events and wire up RabbitMQ

```csharp
builder.UseSchemata(schema => {
    schema.UseEvent()
          .RegisterEvent<OrderPlaced>("orders/order-placed")
          .RegisterEvent<PriceQuery>("orders/price-query")
          .RegisterEvent<PriceResult>("orders/price-result")
          .UseProducer(p => p.UseRabbitMq(o => {
              o.HostName           = "localhost";
              o.ExchangeName       = "schemata.events";
              o.DeadLetterExchange = "schemata.events.dlx";
          }))
          .UseConsumer(c => c.UseRabbitMq())
          .UseHandler<OrderPlaced, OrderPlacedHandler>()
          .UseHandler<PriceQuery, PriceResult, PriceQueryHandler>();
});
```

`RegisterEvent<T>(name)` binds the CLR type to a wire name in `IEventTypeRegistry`. A request/reply
pair needs all three types registered: the request, the response, and any broadcast event the
consumer receives.

`UseRabbitMq()` on the producer registers `RabbitMqEventBus` as a scoped `IEventBus`,
`RabbitMqEventOutboxPublisher` as the outbox publisher, and a `CorrelationTracker`. On the consumer it
registers `RabbitMqConsumerHost` as a hosted service.

**Assertion:** `dotnet run` starts without throwing on `IEventTypeRegistry.RequireName`.

## Step 3: Implement the handlers

```csharp
using Schemata.Event.Skeleton;

public sealed class OrderPlacedHandler : IEventHandler<OrderPlaced>
{
    private readonly ILogger<OrderPlacedHandler> _logger;

    public OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) { _logger = logger; }

    public Task HandleAsync(OrderPlaced @event, CancellationToken ct)
    {
        _logger.LogInformation("Order {Id} placed, total {Total}", @event.OrderId, @event.Total);
        return Task.CompletedTask;
    }
}

public sealed class PriceQueryHandler : IRequestHandler<PriceQuery, PriceResult>
{
    public Task<PriceResult> HandleAsync(PriceQuery request, CancellationToken ct)
        => Task.FromResult(new PriceResult { Price = 9.99m });
}
```

`IEventHandler<T>` handles fire-and-forget events; `IRequestHandler<TRequest, TResponse>` handles
request/reply. Only one request handler per request type may be registered.

**Assertion:** both handler classes compile and their `HandleAsync` methods are reachable.

## Step 4: Publish an event

```csharp
public sealed class OrdersController : ControllerBase
{
    private readonly IEventBus _bus;

    public OrdersController(IEventBus bus) { _bus = bus; }

    [HttpPost("orders")]
    public async Task<IActionResult> PlaceOrder(CancellationToken ct)
    {
        var evt = new OrderPlaced { OrderId = Guid.NewGuid().ToString("n"), Total = 42.00m };
        await _bus.PublishAsync(evt, ct);
        return Accepted();
    }
}
```

`PublishAsync` records the event as a `Pending` outbox row and returns. The `EventOutboxDispatcher`
replays the row through `RabbitMqEventOutboxPublisher`, which opens a publisher-confirm channel,
serializes the payload, and publishes with `DeliveryModes.Persistent`. The publish completes only
after the broker confirms receipt, then the row is marked delivered.

**Assertion:** `POST /orders` returns `202 Accepted` and the management UI shows one message on
`schemata.events` once the dispatcher drains the outbox.

## Step 5: Verify DLX routing

`RabbitMqConsumerHost` declares the main queue with `x-dead-letter-exchange` set to
`RabbitMqEventOptions.DeadLetterExchange` (default `schemata.events.dlx`, a topic exchange). A message
is dead-lettered when:

- The handler throws.
- The routing key resolves to an unregistered event type.
- Deserialization returns null.

To observe it, publish a message with an unregistered routing key from the management UI or
`rabbitmqadmin`. The consumer logs a warning and calls `BasicNackAsync(requeue: false)`, routing the
message to `schemata.events.dlx`.

Set `DeadLetterExchange = string.Empty` to skip the DLX declaration; poison messages are then rejected
without requeue and dropped.

**Assertion:** after publishing a message with routing key `unknown/type`, the `schemata.events.dlx`
exchange receives one message.

## Step 6: Perform a request/reply call

```csharp
[HttpGet("price/{productId}")]
public async Task<IActionResult> GetPrice(string productId, CancellationToken ct)
{
    var result = await _bus.SendAsync<PriceQuery, PriceResult>(
        new PriceQuery { ProductId = productId }, ct);
    return Ok(result);
}
```

`SendAsync` opens a private exclusive auto-delete reply queue named `reply.<guid>` per
`RabbitMqEventBus`, publishes the request with `ReplyTo` and a tracker `CorrelationId`, and awaits a
`TaskCompletionSource<TResponse>` held by `CorrelationTracker`. The consumer host detects `ReplyTo`,
invokes `PriceQueryHandler`, and publishes the response back. The tracker matches by correlation id
and completes the task. Unlike `PublishAsync`, `SendAsync` runs synchronously over the broker rather
than through the outbox.

The timeout is `RabbitMqEventOptions.RequestTimeoutMs` (default 30,000 ms); on timeout the tracker
faults the task with `TimeoutException`.

**Assertion:** `GET /price/widget-1` returns `{"price":9.99}` within the timeout window.

## Common pitfalls

**The bus connects lazily.** `RabbitMqEventBus` and `RabbitMqEventOutboxPublisher` do not open the
broker connection in their constructors — the connection, reply channel, and consumer come up on
the first publish, guarded by a `SemaphoreSlim`. A broker that is down at startup no longer blocks
the host; the failure surfaces on the first publish instead. Because the bus is scoped, each scope
still gets its own connection on first use — inject `IEventBus` into long-lived services
(controllers, background workers) so short-lived scopes don't each pay the connect cost.

**Single handler per request type.** Registering a second `IRequestHandler<TRequest, TResponse>` for
the same pair makes the resolver throw at dispatch ("Multiple request handlers registered"). For
fan-out, use `IEventHandler<T>` with a fire-and-forget event.

**All three types in a req/reply pair must be registered.** `SendAsync` calls `RequireName` on both
the request and the response type. Either missing throws `InvalidOperationException` at the call, not
at startup.

**DLX exchange must exist before the queue is declared.** `RabbitMqConsumerHost` declares the DLX
exchange and binds the queue in `ExecuteAsync`. If the broker already has the queue without
`x-dead-letter-exchange`, RabbitMQ rejects the re-declaration. Delete the queue and restart the
consumer to pick up the new topology.

**`IEventHandler<IEvent>` is a fallback path.** A handler registered for the base `IEvent` interface
catches every event with no more specific handler. Register one only when you intend to intercept all
events.

## See also

- [guides/event-bus.md](../guides/event-bus.md) — in-process event bus basics
- [documents/event/overview.md](../documents/event/overview.md) — wire-name contract and the outbox
- [documents/event/providers.md](../documents/event/providers.md) — InProcess and RabbitMQ providers
- [cookbook/domain-events.md](domain-events.md) — publishing events from a committed advisor
