# RabbitMQ Event Bus

## What you'll build

A Schemata application that publishes an `OrderPlaced` event to RabbitMQ, consumes it in a handler, routes poison messages to a dead-letter exchange, and performs a synchronous request/reply exchange over the same broker. By the end you'll have a working producer, a consumer host, DLX topology, and a typed `SendAsync` call.

## Prerequisites

- A running RabbitMQ broker (default `localhost:5672`, credentials `guest/guest`).
- The `Schemata.Event.RabbitMq` NuGet package added to your project.
- Familiarity with the in-process event bus from [guides/event-bus.md](../guides/event-bus.md).

## Step 1: Define the event and request types

```csharp
using Schemata.Event.Skeleton;

public sealed class OrderPlaced : IEvent
{
    public string OrderId { get; init; } = string.Empty;
    public decimal Total  { get; init; }
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

Every type published over RabbitMQ must implement `IEvent` (fire-and-forget) or `IRequest<TResponse>` (request/reply). The CLR type name is never used as a routing key; you assign the wire name in Step 2.

**Assertion:** the project compiles with no errors referencing `IEvent` or `IRequest<>`.

## Step 2: Register events and wire up RabbitMQ

```csharp
builder.UseSchemata(schema => {
    schema.UseEvent()
          .RegisterEvent<OrderPlaced>("orders/order-placed")
          .RegisterEvent<PriceQuery>("orders/price-query")
          .RegisterEvent<PriceResult>("orders/price-result")
          .UseProducer(p => p.UseRabbitMq(o => {
              o.HostName         = "localhost";
              o.ExchangeName     = "schemata.events";
              o.DeadLetterExchange = "schemata.events.dlx";
          }))
          .UseConsumer(c => c.UseRabbitMq())
          .UseHandler<OrderPlaced, OrderPlacedHandler>()
          .UseHandler<PriceQuery, PriceResult, PriceQueryHandler>();
});
```

`RegisterEvent<T>(name)` binds the CLR type to a wire name stored in `IEventTypeRegistry`. `RequireName(typeof(T))` is called at publish time; an unregistered type throws `InvalidOperationException`. All three types in a request/reply pair must be registered: the request, the response, and any event the consumer might receive.

`UseRabbitMq()` on the producer side registers `RabbitMqEventBus` as a scoped `IEventBus` and a singleton `CorrelationTracker`. On the consumer side it registers `RabbitMqConsumerHost` as a hosted service.

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

`IEventHandler<T>` handles fire-and-forget events. `IRequestHandler<TRequest, TResponse>` handles request/reply; only one handler per request type may be registered — registering a second one silently overwrites the first because `AddScoped` replaces the previous registration.

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

`PublishAsync` opens a channel with publisher confirms enabled (`CreateChannelOptions(publisherConfirmations: true, publisherConfirmationTracking: true)`), serializes the payload to JSON, and calls `BasicPublishAsync` with `DeliveryMode.Persistent`. The `await` returns only after the broker confirms receipt.

**Assertion:** `POST /orders` returns `202 Accepted` and the RabbitMQ management UI shows one message delivered to `schemata.events`.

## Step 5: Verify DLX routing

The consumer host declares the main queue with `x-dead-letter-exchange` set to `RabbitMqEventOptions.DeadLetterExchange` (default `schemata.events.dlx`). Messages are dead-lettered when:

- The handler throws an unhandled exception.
- The routing key resolves to an unregistered event type.
- JSON deserialization returns `null`.

To observe DLX routing, publish a message with an unregistered routing key directly via the management UI or `rabbitmqadmin`. The consumer logs a warning and calls `BasicNackAsync(requeue: false)`, which routes the message to `schemata.events.dlx`.

To disable DLX (accept message loss on poison messages), set `DeadLetterExchange = string.Empty` in `RabbitMqEventOptions`.

**Assertion:** after publishing a message with routing key `unknown/type`, the `schemata.events.dlx` exchange receives one message.

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

`SendAsync` creates a private, exclusive, auto-delete reply queue named `reply.<guid>` per `RabbitMqEventBus` instance. It publishes the request with `ReplyTo` and `CorrelationId` set, then awaits a `TaskCompletionSource<TResponse>` tracked by `CorrelationTracker`. The consumer host detects `ReplyTo` on the incoming message, invokes `PriceQueryHandler`, and publishes the response back to the reply queue. `CorrelationTracker` matches by `CorrelationId` and completes the task.

The timeout is `RabbitMqEventOptions.RequestTimeoutMs` (default 30 000 ms). On timeout, `CorrelationTracker` faults the task with `TimeoutException`.

**Assertion:** `GET /price/widget-1` returns `{"price":9.99}` within the timeout window.

## Common pitfalls

**Scoped bus owns a connection.** `RabbitMqEventBus` is registered as `Scoped`, but its constructor calls `factory.CreateConnectionAsync().GetAwaiter().GetResult()` synchronously. In a high-throughput scenario this blocks a thread pool thread per scope creation. Inject `IEventBus` into long-lived services (controllers, background workers); each short-lived scope would otherwise open its own connection.

**Single handler per request type.** `UseHandler<TRequest, TResponse, THandler>()` calls `AddScoped(typeof(IRequestHandler<,>), typeof(THandler))`. A second call for the same `TRequest`/`TResponse` pair replaces the first. If you need fan-out on a request, use `IEventHandler<T>` with a fire-and-forget event instead.

**All three types in a req/reply pair must be registered.** `SendAsync` calls `_registry.RequireName(typeof(TRequest))` and `_registry.RequireName(typeof(TResponse))`. If either is missing, you get `InvalidOperationException` at call time, not at startup.

**DLX exchange must exist before the queue is declared.** `RabbitMqConsumerHost` declares the DLX exchange and binds the queue in `ExecuteAsync`. If the broker already has the queue without `x-dead-letter-exchange`, RabbitMQ rejects the re-declaration. Delete the queue from the management UI and restart the consumer to pick up the new topology.

**`IEventHandler<IEvent>` is a fallback path.** Registering a handler for the base `IEvent` interface catches every event the consumer receives. `SchemataFlowEventFeature` uses this path internally. Avoid registering your own `IEventHandler<IEvent>` unless you intend to intercept all events.

## See also

- [guides/event-bus.md](../guides/event-bus.md) — in-process event bus basics
- [documents/event/overview.md](../documents/event/overview.md) — wire-name contract and dispatch pipeline
- [documents/event/providers.md](../documents/event/providers.md) — InProcess and RabbitMQ provider details
- [cookbook/domain-events.md](domain-events.md) — publishing events from inside an advisor
