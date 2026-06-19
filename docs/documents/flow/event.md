# Flow Event Integration

`Schemata.Flow.Event` bridges intermediate message and signal catches to the event bus. As a process
transitions, `FlowEventTransitionAdvisor` keeps `IEventSubscriptionStore` in sync with the catches
the instance is waiting on. When a matching event is dispatched, `FlowEventHandler` correlates it
back to the waiting instance through `IProcessRuntime`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Event` | `Features/SchemataFlowEventFeature.cs`, `Internal/FlowEventTransitionAdvisor.cs`, `Internal/FlowEventHandler.cs`, `Models/FlowEventSubscription.cs`, `Extensions/FlowEventBuilderExtensions.cs` |
| `Schemata.Flow.Skeleton` | `Observers/IFlowTransitionAdvisor.cs`, `Observers/FlowTransitionContext.cs` |
| `Schemata.Event.Skeleton` | `IEventSubscriptionStore.cs`, `IEventHandler.cs`, `IEventDispatchContext.cs` |

## Activation

`UseEvent()` chains off the `SchemataFlowBuilder` that `UseFlow` returns:

```csharp
builder.UseSchemata(schema => {
    schema.UseEvent().UseProducer(p => p.UseInProcess()).UseConsumer(c => c.UseInProcess());
    schema.UseFlow()
          .UseEvent()
          .Use<OrderProcess>();
});
```

`UseEvent()` adds `SchemataFlowEventFeature`, priority `SchemataFlowFeature.DefaultPriority + 300_000`
= `480_300_000`. The feature declares `[DependsOn<SchemataFlowFeature>]` and
`[DependsOn<SchemataEventFeature>]`, so both are pulled in if missing; you still need a producer and
consumer transport on the event bus for events to flow.

## What gets registered

`SchemataFlowEventFeature.ConfigureServices` registers two services:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionAdvisor, FlowEventTransitionAdvisor>());
services.TryAddScoped<IEventHandler<IEvent>, FlowEventHandler>();
```

`IEventHandler<IEvent>` is the fallback handler path: a dispatched event with no more specific
handler reaches `FlowEventHandler`.

## FlowEventTransitionAdvisor

`FlowEventTransitionAdvisor` is an `IFlowTransitionAdvisor` (`IAdvisor<FlowTransitionContext>`). Its
`AdviseAsync` runs in the transition's pre-commit window and reconciles `IEventSubscriptionStore`
against the new waiting state, returning `AdviseResult.Continue`.

### Subscription lifecycle

1. **Remove the old subscription** when `PreviousWaitingAtId` is set and differs from the new
   `WaitingAtId`. The advisor resolves the previous element and calls `store.RemoveAsync`. An
   event-based gateway removes the subscription for every outgoing intermediate catch.
2. **Skip the add** when the instance is complete or the new `WaitingAtId` is empty.
3. **Add the new subscription** when the new waiting element is an intermediate catch with a
   definition, or an event-based gateway (one subscription per outgoing catch).

### Subscription format

A subscription is keyed by the waiting element id, so two catches that share an event name in one
process keep distinct subscriptions:

```csharp
// Message catch (point-to-point):
new EventSubscription(
    id:             $"flow:{process.CanonicalName}:{elementId}",
    eventType:      definition.Name,         // BPMN message/signal name
    correlationKey: process.CanonicalName,
    target:         process.CanonicalName);

// Signal catch (broadcast):
new EventSubscription(
    id:             $"flow:{process.CanonicalName}:{elementId}",
    eventType:      definition.Name,
    correlationKey: null,                    // signals do not correlate
    target:         process.CanonicalName);
```

The difference between message and signal is the `CorrelationKey`: a `Message` sets it to the
instance's canonical name (one-to-one); a `Signal` leaves it null (broadcast). `EventType` carries
the BPMN-level name from the model, not the CLR type that implements the event.

## FlowEventHandler

`FlowEventHandler` implements `IEventHandler<IEvent>`. It reads `IEventDispatchContext`'s matched
subscriptions (populated by the bus before handler dispatch) and, for each subscription with a
`Target`:

- `CorrelationKey` set → `IProcessRuntime.CorrelateMessageAsync(target, eventType, @event, ...)`
  (message: one instance).
- `CorrelationKey` null → `IProcessRuntime.ThrowSignalAsync(eventType, @event, ...)` (signal:
  broadcast). Signal throws are de-duplicated by event type, so one dispatched signal raises one
  `ThrowSignalAsync` per distinct name regardless of how many subscriptions matched.

## Extension points

- Implement `IFlowTransitionAdvisor` and register via `TryAddEnumerable` to add subscription logic.
- Implement `IEventSubscriptionStore` to back subscriptions with a durable store; the default
  in-memory store is dropped on restart.

## Caveats

- Subscription reconciliation runs inside the pre-commit advisor pipeline. A subscription is created
  before the transition commits, so a failed commit would roll back the instance row while the
  subscription persists; reconcile by dropping subscriptions whose `Target` resolves to no waiting
  instance.
- `IEventHandler<IEvent>` is the fallback. A more specific `IEventHandler<TEvent>` registration wins
  and `FlowEventHandler` is not invoked for that type.
- Subscription ids use the `flow:{processCanonicalName}:{elementId}` format. Reserve the `flow:`
  prefix for this integration.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [Scheduling Integration](scheduling.md)
- [Event Overview](../event/overview.md)
