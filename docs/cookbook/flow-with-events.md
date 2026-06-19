# Flow with Events

## What you'll build

A BPMN process that parks at an event-based gateway and resumes when either a message or a signal
arrives on the event bus. `UseEvent()` on the flow builder wires `FlowEventTransitionAdvisor`, which
keeps `IEventSubscriptionStore` in sync as the instance waits and advances. Publishing through
`IEventBus` then lets `FlowEventHandler` correlate the event back to the waiting instance.

## Prerequisites

- A working Flow setup from [flow.md](../guides/flow.md).
- A working event bus from [event-bus.md](../guides/event-bus.md).
- `Schemata.Flow.Event` added to the project.

## Step 1: Define the event types

```csharp
using Schemata.Event.Skeleton;

// Correlated to one instance (message semantics).
public sealed class PaymentReceived : IEvent { public string OrderId { get; init; } = ""; }

// Broadcast to all waiting instances (signal semantics).
public sealed class SystemShutdown : IEvent;
```

Message versus signal is a property of the BPMN element, not the CLR type. A `Message` catch creates
a subscription correlated by the instance's canonical name; a `Signal` catch creates a broadcast
subscription with no correlation key.

## Step 2: Model the process

Declare the catches as magic properties and wire them with `.Await(...)`. The first event to fire
wins; the other subscription is removed as the instance advances.

```csharp
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;

public sealed class OrderProcess : ProcessDefinition
{
    public NoneTask New      { get; } = null!;
    public NoneTask Fulfill  { get; } = null!;
    public NoneTask Cancel   { get; } = null!;

    public Message Pay      { get; } = null!;
    public Signal  Shutdown { get; } = null!;

    public OrderProcess()
    {
        Pay.Name      = "payment-received";
        Shutdown.Name = "system-shutdown";

        this.Start().Go(New);

        this.During(New).Await(
            this.On(Pay).Go(Fulfill),
            this.On(Shutdown).Go(Cancel));

        this.During(Fulfill).End();
        this.During(Cancel).End();
    }
}
```

`.Await(...)` builds an event-based gateway with one intermediate catch per branch. The
`Definition.Name` (`payment-received`, `system-shutdown`) is the BPMN-level name the subscription
carries as its `EventType`.

## Step 3: Register and enable flow events

`UseEvent()` chains off the `SchemataFlowBuilder` that `UseFlow` returns:

```csharp
builder.UseSchemata(schema => {
    schema.UseEvent()
          .RegisterEvent<PaymentReceived>("orders/payment-received")
          .RegisterEvent<SystemShutdown>("system/shutdown")
          .UseProducer(p => p.UseInProcess())
          .UseConsumer(c => c.UseInProcess());

    schema.UseFlow()
          .UseEvent()
          .Use<OrderProcess>();
});
```

`UseEvent()` (the flow-builder overload) adds `SchemataFlowEventFeature` (priority `480_300_000`). It
depends on both `SchemataFlowFeature` and `SchemataEventFeature`, so call order does not matter — but
the event bus still needs a producer and consumer for events to flow.

`SchemataFlowEventFeature.ConfigureServices` registers `FlowEventTransitionAdvisor` (a scoped
`IFlowTransitionAdvisor`) and `FlowEventHandler` (a scoped `IEventHandler<IEvent>`, the fallback
handler).

**Check:** the app starts and `IEventSubscriptionStore` resolves from DI.

## Step 4: Start an instance

```csharp
public sealed class OrdersController(IProcessRuntime runtime, IEventBus bus) : ControllerBase
{
    [HttpPost("orders")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        var process = await runtime.StartProcessInstanceAsync(
            "OrderProcess", variables: null, principal: User, ct: ct);
        return Accepted(new { process.State, name = process.CanonicalName });
    }
}
```

After `StartProcessInstanceAsync`, the engine advances through the start event and parks at the
event-based gateway. In the transition's pre-commit window, `FlowEventTransitionAdvisor` adds two
subscriptions:

- `flow:{name}:{payElementId}` with `CorrelationKey = {name}` (message).
- `flow:{name}:{shutdownElementId}` with `CorrelationKey = null` (signal).

The registered process name is the type name, `OrderProcess`.

**Check:** `POST /orders` returns `202`; two rows appear in `IEventSubscriptionStore`.

## Step 5: Correlate a message to advance one instance

```csharp
[HttpPost("orders/{name}/pay")]
public async Task<IActionResult> Pay(string name, CancellationToken ct)
{
    await bus.PublishAsync(new PaymentReceived { OrderId = name }, ct);
    return Accepted();
}
```

When the consumer dispatches `PaymentReceived`, the bus populates
`IEventDispatchContext.MatchedSubscriptions`. `FlowEventHandler.HandleAsync` iterates them; because
the subscription's `CorrelationKey` is non-null, it calls
`runtime.CorrelateMessageAsync(sub.Target, sub.EventType, @event, ct: ct)`. The runtime resolves the
`Message` named `payment-received` and triggers the instance from the gateway to `Fulfill`, then to
the end event.

**Check:** the `SchemataProcess` row for that instance ends with `WaitingAtId = null`; both of its
subscriptions are gone.

## Step 6: Throw a signal to advance every waiting instance

```csharp
[HttpPost("system/shutdown")]
public async Task<IActionResult> Shutdown(CancellationToken ct)
{
    await bus.PublishAsync(new SystemShutdown(), ct);
    return Accepted();
}
```

For signal subscriptions (`CorrelationKey == null`), `FlowEventHandler` calls
`runtime.ThrowSignalAsync(sub.EventType, @event, ct: ct)` — once per distinct event type, even if
many instances are subscribed. `ProcessRuntime.ThrowSignalAsync` finds every waiting instance that
matches `system-shutdown` and advances each through `Cancel` to its end.

**Check:** every instance parked at the gateway transitions to complete via the `Cancel` path.

## Common pitfalls

**The event bus needs a producer and consumer.** `SchemataFlowEventFeature` pulls in
`SchemataEventFeature` through `[DependsOn]`, but without `UseProducer` and `UseConsumer` published
events are dropped. Configure the transports on `UseEvent()`.

**`FlowEventHandler` works off the dispatch context.** It reads
`IEventDispatchContext.MatchedSubscriptions`, which the bus fills before handler dispatch. Calling
`CorrelateMessageAsync` or `ThrowSignalAsync` directly bypasses that context; drive `IProcessRuntime`
yourself for out-of-band correlation.

**Two names, two layers.** `Definition.Name` (`payment-received`) is the BPMN subscription
`EventType`. The wire name registered with `RegisterEvent<T>` (`orders/payment-received`) is the
transport routing key. They are separate identifiers; the handler bridges from the routed event to
the BPMN-level name through the matched subscription.

## See also

- [flow.md](../guides/flow.md) — BPMN basics and `UseFlow`
- [event-bus.md](../guides/event-bus.md) — `UseEvent`, producers, consumers, handlers
- [flow-with-timers.md](flow-with-timers.md) — intermediate timer catches
- [event.md](../documents/flow/event.md) — the advisor and subscription internals
