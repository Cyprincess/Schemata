# RabbitMQ Event Bus

## What you'll build

A Schemata application that publishes an `OrderPlaced` event to RabbitMQ, consumes it in a handler,
dead-letters poison messages, and performs a synchronous request/reply over the same broker. By the
end you'll have a producer, a consumer host, DLX topology, and a typed request dispatcher.

Events and request/reply are **two packages**: `Schemata.Event.RabbitMq` broadcasts, and
`Schemata.Messaging.RabbitMq` does request/reply. They share the one broker connection from
`Schemata.Transport.RabbitMq`.

## Prerequisites

- A running RabbitMQ broker (default `localhost:5672`, credentials `guest/guest`).
- The `Schemata.Event.RabbitMq` package added to your project.
- A persistence provider (EF Core or LinqToDB) so the outbox audit rows can be stored.
- Familiarity with the in-process bus from [guides/event-bus.md](../guides/event-bus.md).

## Step 1: Define the event and request types

```csharp
using Schemata.Event.Skeleton;
using Schemata.Messaging.Skeleton;

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

Fire-and-forget types implement `IEvent` from `Schemata.Event.Skeleton`; request/reply types
implement `IRequest<TResponse>` from `Schemata.Messaging.Skeleton`, which is a separate package so
that request/reply carries no dependency on the event domain. The CLR type name is never the routing
key; the event gets its wire name in Step 2, the request in Step 6.

**Assertion:** the project compiles with no errors referencing `IEvent` or `IRequest<>`.

## Step 2: Register events and wire up RabbitMQ

```csharp
builder.UseSchemata(schema => {
    schema.UseEvent()
          .RegisterEvent<OrderPlaced>("orders/order-placed")
          .UseProducer(p => p.UseRabbitMq(o => {
              o.ExchangeName       = "schemata.events";
              o.DeadLetterExchange = "schemata.events.dlx";
          }, c => {
              c.HostName = "localhost";
          }))
          .UseConsumer(c => c.UseRabbitMq())
       .UseHandler<OrderPlaced, OrderPlacedHandler>();
});
```

`RegisterEvent<T>(name)` binds the CLR type to a wire name in `IEventTypeRegistry`. It covers
broadcast events only — `PriceQuery` and `PriceResult` are never registered here, because the
request dispatcher keeps its own registry (Step 6).

`UseRabbitMq()` on the producer registers `RabbitMqEventBus` as a scoped `IEventBus` and
`RabbitMqEventOutboxPublisher` as the outbox publisher. On the consumer it registers
`RabbitMqConsumerHost` as a hosted service. Both sides also call `AddRabbitMqTransport()`, which
contributes the shared broker connection and the `CorrelationTracker` — the first delegate configures
topology (`RabbitMqEventOptions`), the second the connection (`RabbitMqConnectionOptions`).

**Assertion:** `dotnet run` starts without throwing on `IEventTypeRegistry.RequireName`.

## Step 3: Implement the handlers

```csharp
using Schemata.Event.Skeleton;
using Schemata.Messaging.Skeleton;

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

`IEventHandler<T>` (`Schemata.Event.Skeleton`) handles fire-and-forget events;
`IRequestHandler<TRequest, TResponse>` (`Schemata.Messaging.Skeleton`) handles request/reply. Only
one request handler per request type may be registered.

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
serializes the payload, and publishes with `BasicProperties.DeliveryMode = DeliveryModes.Persistent`.
The publish completes only
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

Request/reply is **not** on the event bus. It is a separate package, `Schemata.Messaging.RabbitMq`,
sharing the same broker connection:

```csharp
schema.ConfigureServices(services =>
    services.AddRabbitMqRequestDispatcher(options => {
        options.QueueName = "pricing";                       // omit on a send-only process
        options.Register<PriceQuery, PriceResult>("pricing.quote");
    }));
```

```csharp
[HttpGet("price/{productId}")]
public async Task<IActionResult> GetPrice(string productId, CancellationToken ct)
{
    var result = await _dispatcher.SendAsync<PriceQuery, PriceResult>(
        new PriceQuery { ProductId = productId }, ct);
    return Ok(result);
}
```

`IRequestDispatcher.SendAsync` opens a private exclusive auto-delete reply queue named
`reply.<guid>` per dispatcher, publishes the request with `ReplyTo` and a tracker `CorrelationId`,
and awaits a `TaskCompletionSource<TResponse>` held by `CorrelationTracker`. The consumer host
resolves `IRequestHandler<PriceQuery, PriceResult>`, invokes it, and publishes the response straight
back to the reply queue. Unlike `PublishAsync`, this runs synchronously over the broker rather than
through the outbox.

The timeout is `RabbitMqRequestOptions.RequestTimeoutMs` (default 30,000 ms); on timeout the tracker
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
the same pair makes the dispatcher throw ("Multiple request handlers registered"). For fan-out, use
`IEventHandler<T>` with a fire-and-forget event.

**The request type must carry a registered wire name.** `Register<TRequest, TResponse>(name)` in
`AddRabbitMqRequestDispatcher` is mandatory — a CLR type name never travels on the wire. Sending an
unregistered request throws `InvalidOperationException` at the call, naming the type. Note the
response type needs **no** name: replies go straight to the caller's exclusive reply queue, matched
by correlation id.

**Events and requests use separate registries.** `RegisterEvent<T>(name)` covers the bus;
`Register<TRequest, TResponse>(name)` covers the request dispatcher. Registering a request as an
event does not make it dispatchable, and vice versa.

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
