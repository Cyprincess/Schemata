# Flow Event Integration

`Schemata.Flow.Event` bridges the Flow engine with the event bus. When a process instance reaches an `IntermediateCatchEvent` or an `EventBasedGateway`, `FlowEventTransitionObserver` registers subscriptions in `IEventSubscriptionStore`. When a matching event arrives on the bus, `FlowEventHandler` resolves the waiting instance and calls `IProcessRuntime.CorrelateMessageAsync` (for `Message`) or `ThrowSignalAsync` (for `Signal`).

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Event` | `Features/SchemataFlowEventFeature.cs`, `Internal/FlowEventTransitionObserver.cs`, `FlowEventHandler.cs`, `Models/FlowEventSubscription.cs`, `Extensions/FlowEventBuilderExtensions.cs` |
| `Schemata.Flow.Skeleton` | `Observers/IFlowTransitionObserver.cs`, `Observers/FlowTransitionContext.cs` |
| `Schemata.Event.Skeleton` | `IEventSubscriptionStore.cs`, `IEventSubscription.cs`, `IEventHandler.cs` |

## Activation

```csharp
builder.UseSchemata(schema => {
    schema.UseFlow(flow => flow.Use<OrderProcess>());
    schema.UseFlowEvent();
});
```

`UseFlowEvent` adds `SchemataFlowEventFeature` (Priority `SchemataFlowFeature.DefaultPriority + 300_000` = 480,300,000). The feature declares `[DependsOn<SchemataFlowFeature>]` and `[DependsOn<SchemataEventFeature>]`, so both are auto-pulled if not already registered.

## What gets registered

`SchemataFlowEventFeature.ConfigureServices` registers two services:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionObserver, FlowEventTransitionObserver>());
services.TryAddScoped<IEventHandler<IEvent>, FlowEventHandler>();
```

`IEventHandler<IEvent>` is the fallback handler path: any event that has no more specific handler registered will be routed here. `FlowEventHandler` looks up subscriptions by event type and correlates the event to the matching process instance.

## FlowEventTransitionObserver

`FlowEventTransitionObserver` implements `IFlowTransitionObserver` and keeps `IEventSubscriptionStore` in sync with the waiting state on every transition.

### Subscription lifecycle

`OnTransitionedAsync` calls `IEventSubscriptionStore` directly. Each step is conditional:

1. **Remove old subscriptions** when `context.PreviousWaitingAtId` is set and differs from `context.Instance.WaitingAtId`. The observer resolves the previous element from the definition and calls `store.RemoveAsync(...)` with the subscription id `flow:{processCanonicalName}:{definition.Name}`. An `EventBasedGateway` previous element removes the subscription for every outgoing intermediate catch.
2. **Skip the add step** when `context.Instance.IsComplete` is true or the new `WaitingAtId` is empty.
3. **Add new subscriptions** when the new waiting element is an `IntermediateCatchEvent` with a non-null `Definition`: the observer calls `store.AddAsync(...)` with `EventType = definition.Name` and `CorrelationKey = process.CanonicalName` for `Message`, `null` for `Signal`. An `EventBasedGateway` adds one subscription per outgoing catch event.

### Subscription format

```csharp
// For a Message event (point-to-point):
var subscription = new EventSubscription(
    id:             $"flow:{process.CanonicalName}:{definition.Name}",
    eventType:      definition.Name,   // BPMN message/signal name
    correlationKey: process.CanonicalName,
    target:         process.CanonicalName);

// For a Signal event (broadcast):
var subscription = new EventSubscription(
    id:             $"flow:{process.CanonicalName}:{definition.Name}",
    eventType:      definition.Name,
    correlationKey: null,              // no correlation for signals
    target:         process.CanonicalName);
```

`EventType` carries the BPMN message/signal name so consumers correlate by the domain identifier the model defines, not by the CLR type implementing it.

## FlowEventHandler

`FlowEventHandler` implements `IEventHandler<IEvent>` and is the fallback handler for all events. It reads `IEventDispatchContext.MatchedSubscriptions` (populated by the bus before handler dispatch) and, for each subscription whose `Target` is set:

- Calls `IProcessRuntime.CorrelateMessageAsync(sub.Target, sub.EventType, @event, ct)` when `CorrelationKey` is non-null (message semantics).
- Calls `IProcessRuntime.ThrowSignalAsync(sub.EventType, @event, ct)` otherwise (signal semantics).

## Extension points

- Implement `IFlowTransitionObserver` and register via `TryAddEnumerable` to add custom subscription management logic.
- Implement `IEventSubscriptionStore` to back subscriptions with a durable store. The default `InMemoryEventSubscriptionStore` is dropped on restart.

## Caveats

- `IEventSubscriptionStore` mutations run inside `OnTransitionedAsync` before `IProcessLifecycleObserver` dispatch. A throwing audit observer leaves the subscription store ahead of the audit row.
- `IEventHandler<IEvent>` is the fallback path. A more specific `IEventHandler<TEvent>` registration wins and `FlowEventHandler` is not invoked.
- Subscription IDs use the format `flow:{processCanonicalName}:{eventDefinitionName}`. Reserve the `flow:` prefix for the integration.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [Event Overview](../event/overview.md)
- [Event Providers](../event/providers.md)
- [Scheduling Integration](scheduling.md)
