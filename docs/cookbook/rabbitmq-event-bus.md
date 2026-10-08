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
- A persistence provider (EF Core or LinqToDB) with repositories for `SchemataEvent` and
  `SchemataEventSubscription`, plus the [event audit Name advisor](domain-events.md#step-7-name-the-event-audit-row).
- A persisted `SchemataEventSubscription` with `EventType = "orders/order-placed"`, a unique
  `SubscriptionId`, and the application's target. `UseHandler` registers DI handlers, not subscription
  rows; the RabbitMQ consumer acknowledges and drops registered events without a matching row.
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

Broadcast types implement `IEvent` from `Schemata.Event.Skeleton`; request/reply types
implement `IRequest<TResponse>` from `Schemata.Messaging.Skeleton`, which is a separate package so
that request/reply carries no dependency on the event domain. The CLR type name is never the routing
key; the event gets its wire name in Step 2, the request in Step 6.

**Assertion:** the project compiles with no errors referencing `IEvent` or `IRequest<>`.

## Step 2: Register events and wire up RabbitMQ

```csharp
using Microsoft.AspNetCore.Builder;

builder.UseSchemata(schema => {
    schema.UseEvent()
          .RegisterEvent<OrderPlaced>("orders/order-placed")
          .UseProducer(p => p.UseRabbitMq(o => {
              o.ExchangeName       = "schemata.events";
              o.DeadLetterExchange = "schemata.events.dlx";
          }, c => {
              c.HostName = "localhost";
          }))
          .UseConsumer(c => c.UseInProcess().UseRabbitMq())
       .UseHandler<OrderPlaced, OrderPlacedHandler>();
});
```

`RegisterEvent<T>(name)` binds the CLR type to a wire name in `IEventTypeRegistry`. It covers
broadcast events only — `PriceQuery` and `PriceResult` are never registered here, because the
request dispatcher keeps its own registry (Step 6).

`UseRabbitMq()` on the producer registers `RabbitMqEventBus` as a scoped `IEventBus`. On the
consumer it registers `RabbitMqConsumerHost` as a hosted service; `UseInProcess()` supplies its
handler resolver and dispatch context. Both RabbitMQ extensions call `AddRabbitMqTransport()`
for the shared connection provider and `CorrelationTracker`. The first delegate configures
topology (`RabbitMqEventOptions`), and the second configures the connection (`RabbitMqConnectionOptions`).

**Assertion:** `dotnet run` starts without throwing on `IEventTypeRegistry.RequireName`.

## Step 3: Implement the handlers

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

`IEventHandler<T>` (`Schemata.Event.Skeleton`) handles broadcast events;
`IRequestHandler<TRequest, TResponse>` (`Schemata.Messaging.Skeleton`) handles request/reply. Only
one request handler per request type may be registered.

**Assertion:** both handler classes compile and their `HandleAsync` methods are reachable.

## Step 4: Publish an event

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Event.Skeleton;

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

`PublishAsync` runs publish advisors and audit observers, opens a publisher-confirm channel,
and publishes a persistent message to the exchange. It awaits broker confirmation, then runs
`OnDeliveredAsync` observers before returning. Consumer handling runs separately.

The audit row is committed as `Recorded` before broker publication and is not a durable delivery
queue. The bus supplies neither a transactional outbox nor automatic publish retries. A business
transaction and broker acceptance remain separate, and a failed post-confirm observer can fault
the call after the broker has accepted the message.

**Assertion:** after the consumer has declared and bound its queue and the matching subscription
exists, `POST /orders` returns `202 Accepted` after broker confirmation, and the consumer logs
the order. A fast consumer can leave the queue empty by the time you inspect it.

## Step 5: Verify DLX routing

`RabbitMqConsumerHost` declares the main queue with `x-dead-letter-exchange` set to
`RabbitMqEventOptions.DeadLetterExchange` (default `schemata.events.dlx`, a topic exchange).
It rejects unknown routing keys without requeue. For registered events with matching persisted
subscriptions, it also rejects handler, consume-advisor, observer, and deserialization failures.
Registered events with no matching subscriptions are acknowledged and dropped instead.

To observe a rejected message, first declare a durable inspection queue in the management UI and
bind it to `schemata.events.dlx` with routing key `#`. The consumer declares the DLX but does not
create or bind a dead-letter queue. Publish a message with an unregistered routing key from the
management UI or `rabbitmqadmin`. The consumer logs a warning and calls
`BasicNackAsync(requeue: false)`.

Setting `DeadLetterExchange = string.Empty` skips the DLX declaration and queue argument; without
a broker-supplied dead-letter policy, rejected messages are discarded.

**Assertion:** the inspection queue receives the message published with routing key `unknown/type`.

## Step 6: Perform a request/reply call

`Schemata.Messaging.RabbitMq` provides request/reply over the shared broker connection. Add that
package for this optional step, register the request handler, and configure its wire name:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;

schema.ConfigureServices(services => {
    services.AddScoped<IRequestHandler<PriceQuery, PriceResult>, PriceQueryHandler>();
    services.AddRabbitMqRequestDispatcher(options => {
        options.QueueName = "pricing";
        options.Register<PriceQuery, PriceResult>("pricing.quote");
    });
});
```

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Messaging.Skeleton;

public sealed class PricingController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet("price/{productId}")]
    public async Task<IActionResult> GetPrice(string productId, CancellationToken ct)
    {
        var result = await dispatcher.SendAsync<PriceQuery, PriceResult>(
            new PriceQuery { ProductId = productId }, ct);
        return Ok(result);
    }
}
```

`IRequestDispatcher.SendAsync` opens a private exclusive auto-delete reply queue named
`reply.<guid>` per dispatcher, publishes the request with `ReplyTo` and a tracker `CorrelationId`,
and awaits a `TaskCompletionSource<TResponse>` held by `CorrelationTracker`. The consumer host
resolves `IRequestHandler<PriceQuery, PriceResult>`, invokes it, and publishes the response straight
back to the reply queue. A request waits for that response; event `PublishAsync` waits only for
broker confirmation and its producer-side observers.

The timeout is `RabbitMqRequestOptions.RequestTimeoutMs` (default 30,000 ms); on timeout the tracker
faults the task with `TimeoutException`.

**Assertion:** `GET /price/widget-1` returns `{"price":9.99}` within the timeout window.

## Common pitfalls

**Connection lifetime differs from bus scope.** `RabbitMqEventBus` uses the shared connection
provider and opens a publisher-confirm channel per publish. The provider connects lazily under a
semaphore; event bus scopes share its connection. A producer-only host connects on first publish,
while `RabbitMqConsumerHost` requests the connection when its background service starts. Broker
unavailability can therefore fail the consumer service before any event is published.

**Single handler per request type.** Registering a second `IRequestHandler<TRequest, TResponse>` for
the same pair makes the dispatcher throw ("Multiple request handlers registered"). For fan-out, use
`IEventHandler<T>` with a broadcast event.

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
- [documents/event/overview.md](../documents/event/overview.md) — wire-name contract and audit records
- [documents/event/providers.md](../documents/event/providers.md) — InProcess and RabbitMQ providers
- [cookbook/domain-events.md](domain-events.md) — publishing events from a committed advisor
