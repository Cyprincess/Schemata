# Flow with Events

## What you'll build

A BPMN process that pauses at an `eventBasedGateway` and resumes when either a message or a signal arrives from the event bus. You'll wire `UseFlowEvent()` so `FlowEventTransitionObserver` keeps `IEventSubscriptionStore` in sync as the process transitions, then publish events through `IEventBus` and watch `FlowEventHandler` correlate them back to the waiting instance.

## Prerequisites

- A working Flow setup from [guides/flow.md](../guides/flow.md).
- A working event bus setup from [guides/event-bus.md](../guides/event-bus.md).
- `Schemata.Flow.Event` NuGet package added to your project.
- Both `UseEvent()` and `UseFlowEvent()` called during startup (see Step 2).

## Step 1: Define the event types

```csharp
using Schemata.Event.Skeleton;

// Sent to one specific process instance (message semantics)
public sealed class PaymentReceived : IEvent
{
    public string OrderId { get; init; } = string.Empty;
}

// Broadcast to all waiting instances (signal semantics)
public sealed class SystemShutdown : IEvent { }
```

The distinction between message and signal is determined by the BPMN element type, not the CLR type. A `Message` element in the process definition creates a one-to-one subscription keyed by the process instance's canonical name. A `Signal` element creates a broadcast subscription with no correlation key.

**Assertion:** both types compile and implement `IEvent`.

## Step 2: Define the process with an event-based gateway

```csharp
using Schemata.Flow.Skeleton.Models;

public sealed class OrderProcess : IProcessDefinition
{
    public string Name => "order";

    public ProcessDefinition Build()
    {
        var start   = new StartEvent { Id = "start" };
        var gateway = new EventBasedGateway { Id = "wait-for-event" };

        var catchPayment = new FlowEvent {
            Id       = "catch-payment",
            Position = EventPosition.IntermediateCatch,
            Definition = new Message { Name = "payment-received" },
        };

        var catchShutdown = new FlowEvent {
            Id       = "catch-shutdown",
            Position = EventPosition.IntermediateCatch,
            Definition = new Signal { Name = "system-shutdown" },
        };

        var fulfill  = new Activity { Id = "fulfill",  Name = "Fulfill order" };
        var cancel   = new Activity { Id = "cancel",   Name = "Cancel order" };
        var end      = new EndEvent { Id = "end" };

        return new ProcessDefinition {
            Name     = "order",
            Elements = [start, gateway, catchPayment, catchShutdown, fulfill, cancel, end],
            Flows    = [
                new SequenceFlow { Source = start,        Target = gateway      },
                new SequenceFlow { Source = gateway,      Target = catchPayment  },
                new SequenceFlow { Source = gateway,      Target = catchShutdown },
                new SequenceFlow { Source = catchPayment, Target = fulfill       },
                new SequenceFlow { Source = catchShutdown, Target = cancel       },
                new SequenceFlow { Source = fulfill,      Target = end           },
                new SequenceFlow { Source = cancel,       Target = end           },
            ],
            Messages = [new Message { Name = "payment-received" }],
            Signals  = [new Signal  { Name = "system-shutdown"  }],
        };
    }
}
```

The `EventBasedGateway` has two outgoing flows, each targeting an `IntermediateCatch` event. When the process reaches the gateway, `FlowEventTransitionObserver` writes subscriptions for both `payment-received` and `system-shutdown` to `IEventSubscriptionStore`. Whichever event arrives first wins; the other subscription is removed when the process advances.

**Assertion:** `Build()` returns a `ProcessDefinition` with seven elements, two messages, and one signal.

## Step 3: Register the process and enable flow events

```csharp
builder.UseSchemata(schema => {
    schema.UseEvent()
          .RegisterEvent<PaymentReceived>("orders/payment-received")
          .RegisterEvent<SystemShutdown>("system/shutdown")
          .UseProducer(p => p.UseInProcess())
          .UseConsumer(c => c.UseInProcess());

    schema.UseFlow(flow => flow.Use<OrderProcess>());
    schema.UseFlowEvent();
});
```

`UseFlowEvent()` adds `SchemataFlowEventFeature` (priority `SchemataFlowFeature.DefaultPriority + 300_000` = 480,300,000). It depends on both `SchemataFlowFeature` and `SchemataEventFeature` via `[DependsOn<T>]`, so the order of `UseFlow` and `UseEvent` calls does not matter.

`SchemataFlowEventFeature.ConfigureServices` registers two services:

- `FlowEventTransitionObserver` as a scoped `IFlowTransitionObserver` — adds or removes subscriptions whenever the engine reports a transition.
- `FlowEventHandler` as a scoped `IEventHandler<IEvent>` — the fallback handler that receives every dispatched event and routes it to `CorrelateMessageAsync` or `ThrowSignalAsync`.

`FlowEventHandler` reads `IEventDispatchContext.MatchedSubscriptions` to find which process instances are waiting for the arriving event. Subscriptions with a non-null `CorrelationKey` use message semantics (one-to-one); subscriptions without one use signal semantics (broadcast).

**Assertion:** the application starts and `IEventSubscriptionStore` is resolvable from DI.

## Step 4: Start a process instance

```csharp
public sealed class OrdersController : ControllerBase
{
    private readonly IProcessRuntime _runtime;
    private readonly IEventBus       _bus;

    public OrdersController(IProcessRuntime runtime, IEventBus bus)
    {
        _runtime = runtime;
        _bus     = bus;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        var instance = await _runtime.StartProcessInstanceAsync(
            "order", variables: null, principal: User, ct);
        return Accepted(new { instance.StateId, instanceName = instance.CanonicalName });
    }
}
```

After `StartProcessInstanceAsync`, the engine advances through `start` and stops at `wait-for-event`. `FlowEventTransitionObserver` calls `IEventSubscriptionStore.AddAsync` twice:

- `flow:<instanceName>:payment-received` with `CorrelationKey = <instanceName>` (message).
- `flow:<instanceName>:system-shutdown` with `CorrelationKey = null` (signal).

**Assertion:** `POST /orders` returns `202 Accepted` with `stateId = "wait-for-event"`. Two rows appear in the `IEventSubscriptionStore` backing table.

## Step 5: Correlate a message to advance one instance

```csharp
[HttpPost("orders/{instanceName}/pay")]
public async Task<IActionResult> Pay(string instanceName, CancellationToken ct)
{
    var evt = new PaymentReceived { OrderId = instanceName };
    await _bus.PublishAsync(evt, ct);
    return Accepted();
}
```

When `IEventBus.PublishAsync` dispatches `PaymentReceived`, the consumer resolves `IEventDispatchContext` and calls `context.SetSubscriptions(subscriptions)` with the matching rows from `IEventSubscriptionStore`. `FlowEventHandler.HandleAsync` iterates the subscriptions. Because `CorrelationKey` is non-null, it calls:

```csharp
await _runtime.CorrelateMessageAsync(sub.Target, sub.EventType, @event, ct: ct);
```

`ProcessRuntime.CorrelateMessageAsync` loads the process instance by `instanceName`, finds the `Message` definition named `payment-received`, and calls `runtime.TriggerAsync` to advance the process from `catch-payment` to `fulfill` and then to `end`.

**Assertion:** after `POST /orders/{instanceName}/pay`, the `SchemataProcess` row has `WaitingAtId = null` and `IsComplete = true`. The `payment-received` and `system-shutdown` subscriptions for this instance are removed.

## Step 6: Throw a signal to advance all waiting instances

```csharp
[HttpPost("system/shutdown")]
public async Task<IActionResult> Shutdown(CancellationToken ct)
{
    var evt = new SystemShutdown();
    await _bus.PublishAsync(evt, ct);
    return Accepted();
}
```

`FlowEventHandler` finds all subscriptions for `system-shutdown`. Because `CorrelationKey` is null, it calls:

```csharp
await _runtime.ThrowSignalAsync(sub.EventType, @event, ct: ct);
```

`ProcessRuntime.ThrowSignalAsync` queries all `SchemataProcess` rows where `WaitingAtId != null`, checks each against the signal definition, and advances every matching instance from `catch-shutdown` to `cancel` and then to `end`.

**Assertion:** after `POST /system/shutdown`, every process instance waiting at `wait-for-event` transitions to `IsComplete = true` via the `cancel` path.

## Common pitfalls

**Both `UseEvent` and `UseFlowEvent` are required.** `SchemataFlowEventFeature` depends on `SchemataEventFeature` via `[DependsOn<SchemataEventFeature>]`. If you call only `UseFlowEvent()` without `UseEvent()`, the dependency resolver pulls in `SchemataEventFeature` automatically, but no producer or consumer transport is registered. Events published via `IEventBus` will be silently dropped unless you also call `UseProducer` and `UseConsumer`.

**`IEventDispatchContext` is scoped.** `FlowEventHandler` receives `IEventDispatchContext` via constructor injection. The bus populates `MatchedSubscriptions` before handler dispatch. Calling `CorrelateMessageAsync` or `ThrowSignalAsync` directly from application code bypasses the dispatch path; `MatchedSubscriptions` stays empty and `FlowEventHandler` short-circuits. Drive `IProcessRuntime` directly for out-of-band correlation.

**Subscription cleanup on process completion.** `FlowEventTransitionObserver` removes the previous subscription when `PreviousWaitingAtId` differs from the current `WaitingAtId`. A `TerminateProcessInstanceAsync` call goes through the same observer dispatch and the subscription disappears with the transition. Orphans only appear when the audit observer fails persistence after the subscription store has been mutated; reconcile by reading the subscription store and removing entries whose `Target` resolves to a completed process.

**Message vs. signal naming.** The `Definition.Name` on the BPMN element (`"payment-received"`) is the subscription `EventType`, not the wire name registered with `RegisterEvent<T>`. The wire name (`"orders/payment-received"`) is the routing key used by the transport. `FlowEventHandler` receives the event after the transport has already routed it; it uses `IEventDispatchContext.MatchedSubscriptions` to find the BPMN-level name. Keep the two names consistent in your mental model but understand they are separate identifiers.

## See also

- [guides/flow.md](../guides/flow.md) — BPMN process basics and `UseFlow`
- [guides/event-bus.md](../guides/event-bus.md) — `UseEvent`, producers, consumers, handlers
- [cookbook/flow-with-timers.md](flow-with-timers.md) — intermediate timer events
- [cookbook/rabbitmq-event-bus.md](rabbitmq-event-bus.md) — RabbitMQ transport for cross-service events
- [documents/flow/event.md](../documents/flow/event.md) — `FlowEventTransitionObserver` and subscription store internals
- [documents/event/overview.md](../documents/event/overview.md) — wire-name contract and dispatch pipeline
