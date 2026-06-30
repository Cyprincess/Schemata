# Flow with Events

## What you'll build

A BPMN process that parks at an event-based gateway and resumes when either a message or a signal
arrives on the event bus. `UseEvent()` on the flow builder wires `AdviceTransitionEvent`, which
upserts and removes `IRepository<SchemataEventSubscription>` rows as the instance waits and
advances. Publishing through `IEventBus` lets `FlowEventHandler` correlate the event back to the
waiting instance.

## Prerequisites

- A working Flow setup from [flow.md](../guides/flow.md).
- A working event bus from [event-bus.md](../guides/event-bus.md).
- `Schemata.Flow.Event` added to the project.
- `Schemata.Flow.Http` added to the project (the start call in Step 4 uses its endpoint).

## Step 1: Define the event types

```csharp
using Schemata.Event.Skeleton;

// Correlated to one instance (message semantics).
public sealed class PaymentReceived : IEvent { public string OrderId { get; init; } = ""; }

// Broadcast to all waiting instances (signal semantics).
public sealed class SystemShutdown : IEvent;
```

`IEvent` is a marker interface with no members. `PaymentReceived` and `SystemShutdown` are
distinguished at the BPMN level by which catch event they back; the advisor keys off the
subscription's `EventType`, so the CLR types serve only as bus routing markers.

Message versus signal is a property of the BPMN element, not the CLR type. A `Message` catch creates
a subscription correlated by the instance canonical name; a `Signal` catch creates a broadcast
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

`.Await(...)` builds an event-based gateway with one intermediate catch per branch. `Message.Name`
and `Signal.Name` (`payment-received`, `system-shutdown`) are the BPMN-level names the
subscription carries as its `EventType`. `Name` is settable on `Message` and `Signal` after
construction.

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
          .UseStateMachine()
          .UseEvent()
          .MapHttp()
          .Use<OrderProcess>();
});
```

`MapHttp()` exposes the process verbs over HTTP; the start call in Step 4 goes through it.

The flow-builder `UseEvent()` adds `SchemataFlowEventFeature` (priority `480_300_000`). It depends
on both `SchemataFlowFeature` and `SchemataEventFeature`, so `UseEvent()` composes at any position
in the builder chain. The bus still needs a producer and a consumer for events to flow.

`SchemataFlowEventFeature.ConfigureServices` registers `AdviceTransitionEvent` as a scoped
`IFlowTransitionAdvisor` and `FlowEventHandler` as a scoped `IEventHandler<IEvent>`.

**Check:** the app starts and `IRepository<SchemataEventSubscription>` resolves from DI.

## Step 4: Start an instance

`Schemata.Flow.Http` provides the start endpoint:

```
POST /v1/processes:start
Content-Type: application/json

{ "definitionName": "OrderProcess" }
```

The engine advances through the start event and parks at the event-based gateway.
`AdviceTransitionEvent` runs inside the transition's unit of work and adds two subscriptions that
commit atomically with the transition:

- `flow:{processName}:{payElementId}` with `CorrelationKey = {processName}` for the message catch.
- `flow:{processName}:{shutdownElementId}` with `CorrelationKey = null` for the signal catch.

The registered process name is the type name, `OrderProcess`.

**Check:** `POST /v1/processes:start` returns `200 OK` with the process row; two
`SchemataEventSubscription` rows appear in the repository.

## Step 5: Correlate a message to advance one instance

Publishing the business event is application code; only this part needs a controller:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Event.Skeleton;

public sealed class OrdersController(IEventBus bus) : ControllerBase
{
    [HttpPost("orders/{name}/pay")]
    public async Task<IActionResult> Pay(string name, CancellationToken ct) {
        await bus.PublishAsync(new PaymentReceived { OrderId = name }, ct);
        return Accepted();
    }
}
```

When the consumer dispatches `PaymentReceived`, the bus populates
`IEventDispatchContext.MatchedSubscriptions`. `FlowEventHandler.HandleAsync` iterates them. Because
the subscription's `CorrelationKey` is non-null, it invokes `CorrelateMessageHandler` with
`MessageName = sub.EventType` and `Payload = null`. `FlowEventHandler` forwards only the BPMN-level
event name and drops the event body; a process that needs the payload takes it through a separate
`IEventHandler<TEvent>` you write, which binds the source itself.

The correlated instance advances from the gateway to `Fulfill`, then to the end event.

**Check:** the `SchemataProcess` row for that instance ends with `WaitingAtName = null`; both of its
subscriptions are gone.

## Step 6: Throw a signal to advance every waiting instance

Add a second action to the same controller:

```csharp
[HttpPost("system/shutdown")]
public async Task<IActionResult> Shutdown(CancellationToken ct) {
    await bus.PublishAsync(new SystemShutdown(), ct);
    return Accepted();
}
```

For signal subscriptions, `CorrelationKey` is null. `FlowEventHandler` invokes `ThrowSignalHandler`
once per distinct `EventType`, with `Payload = null`. The handler drives `FlowRunner.ThrowSignalAsync`,
which enumerates waiting processes whose definition declares the named signal and advances each one
through `Cancel` to its end.

**Check:** every instance parked at the gateway transitions to complete via the `Cancel` path.

## Common pitfalls

**The event bus needs a producer and consumer.** `SchemataFlowEventFeature` pulls in
`SchemataEventFeature` through `[DependsOn]`, but without `UseProducer` and `UseConsumer` published
events are dropped. Configure the transports on the schema-level `UseEvent()`.

**`FlowEventHandler` works off the dispatch context.** It reads
`IEventDispatchContext.MatchedSubscriptions`, which the bus fills before handler dispatch. Calling
`FlowRunner.CorrelateAsync` or `FlowRunner.ThrowSignalAsync` directly bypasses that context; use
them for out-of-band correlation from your own code paths. Inject `FlowRunner` rather than
`IFlowRunner` to access those methods.

**Two names, two layers.** `Message.Name` / `Signal.Name` (`payment-received`) is the BPMN
subscription `EventType`. The wire name registered with `RegisterEvent<T>` (`orders/payment-received`)
is the transport routing key. They are separate identifiers; the handler bridges from the routed
event to the BPMN-level name through the matched subscription row.

**`Payload = null` on Flow dispatch.** `FlowEventHandler` invokes `CorrelateMessageHandler` and
`ThrowSignalHandler` with a null payload. The process receives only the BPMN-level event name; the
CLR `PaymentReceived` instance the publisher passed to `bus.PublishAsync` stays on the bus side. To
carry the payload onto the BPMN side, register `Message<TPayload>` and let the typed message flow
through your own handler.

**A bound source shows the business node, never the gateway.** While the instance waits at the
event-based gateway, a projected source state member carries the name of the activity the token
arrived from (`New` in this recipe), not the synthetic `Await_*` gateway name; the gateway name
lives on the token row's `WaitingAtName`. Under the default `Auto` projection the member flips to
the process lifecycle state (`Completed`) once the process ends. Treat projected values as part of
your API surface: renaming `New` or `Fulfill` changes what consumers read from the source row.

## See also

- [flow.md](../guides/flow.md) — BPMN basics and `UseFlow`
- [event-bus.md](../guides/event-bus.md) — `UseEvent`, producers, consumers, handlers
- [flow-with-timers.md](flow-with-timers.md) — intermediate timer catches
- [event.md](../documents/flow/event.md) — the advisor and subscription internals
