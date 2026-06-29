# Flow Event Integration

`Schemata.Flow.Event` bridges BPMN message and signal catches to the event bus. As a process
transitions, `FlowEventTransitionAdvisor` keeps `IRepository<SchemataEventSubscription>` in sync
with the catches the instance is waiting on. When a matching event is dispatched, `FlowEventHandler`
correlates it back to the waiting instance through `IProcessRuntime`. The same package also
publishes process lifecycle notifications through `ProcessEventLifecycleObserver`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Event` | `Features/SchemataFlowEventFeature.cs`, `Internal/FlowEventTransitionAdvisor.cs`, `Internal/FlowEventHandler.cs`, `Internal/ProcessEventLifecycleObserver.cs`, `Extensions/FlowEventBuilderExtensions.cs` |
| `Schemata.Flow.Skeleton` | `Observers/IFlowTransitionAdvisor.cs`, `Observers/FlowTransitionContext.cs`, `Runtime/IProcessLifecycleObserver.cs` |
| `Schemata.Event.Skeleton` | `Entities/SchemataEventSubscription.cs`, `IEventHandler.cs`, `IEventDispatchContext.cs` |
| `Schemata.Event.Foundation` | `SchemataEventSubscriptionExtensions.cs` |

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
`[DependsOn<SchemataEventFeature>]`, so both are pulled in if missing. You still need a producer and
consumer transport on the event bus for events to move between publishers and consumers.

## What gets registered

`SchemataFlowEventFeature.ConfigureServices` registers three scoped services and four event type
aliases:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionAdvisor,
    FlowEventTransitionAdvisor>());
services.TryAddEnumerable(ServiceDescriptor.Scoped<IProcessLifecycleObserver,
    ProcessEventLifecycleObserver>());
services.TryAddScoped<IEventHandler<IEvent>, FlowEventHandler>();

services.Configure<EventTypeRegistryConfiguration>(options => {
    options.Registrations.Add((typeof(ProcessStartedEvent), "schemata/flow/process.started"));
    options.Registrations.Add((typeof(ProcessCompletedEvent), "schemata/flow/process.completed"));
    options.Registrations.Add((typeof(ProcessFailedEvent), "schemata/flow/process.failed"));
    options.Registrations.Add((typeof(TransitionMadeEvent), "schemata/flow/transition.made"));
});
```

`FlowEventTransitionAdvisor` manages message and signal wake-up subscriptions before a transition
commits. `ProcessEventLifecycleObserver` publishes Flow lifecycle events after runtime persistence
succeeds or fails. `FlowEventHandler` is the generic `IEvent` handler that receives matched event-bus
subscriptions and wakes the waiting process instances.

## FlowEventTransitionAdvisor

`FlowEventTransitionAdvisor` is an `IFlowTransitionAdvisor` (`IAdvisor<FlowTransitionContext>`). Its
`AdviseAsync` runs inside the transition's unit of work, before the process row is persisted, and
reconciles `IRepository<SchemataEventSubscription>` against the new waiting state. It enlists the
subscription repository in `context.UnitOfWork` via `IRepository.Join`, so subscription writes
commit atomically with the process row and roll back together on any failure. Returns
`AdviseResult.Continue`.

### Subscription lifecycle

1. **Remove old subscriptions** when `PreviousWaitingAtId` is set and differs from the new
   `WaitingAtId`. The advisor resolves the previous element, looks up each subscription by
   `SubscriptionId`, and removes the matching row through `IRepository.RemoveAsync`. An event-based
   gateway removes the subscription for every outgoing intermediate catch.
2. **Skip the add** when the instance is complete or the new `WaitingAtId` is empty.
3. **Add new subscriptions** when the new waiting element is an intermediate catch with a definition,
   or an event-based gateway. The advisor upserts each subscription by `SubscriptionId`
   (`IRepository.FirstOrDefaultAsync` then `AddAsync` or `UpdateAsync`). A gateway adds one
   subscription per outgoing intermediate catch.

The default state-machine engine waits at the host activity for boundary message and signal events,
so this advisor registers intermediate catches and event-based gateway branches. A custom keyed
`IFlowRuntime` can follow the same subscription pattern for boundary catches if it parks tokens on
those catches.

### Subscription format

`FlowEventTransitionAdvisor` writes `SchemataEventSubscription` rows through
`IRepository<SchemataEventSubscription>`. Each row carries `SubscriptionId`, `EventType`,
`CorrelationKey`, and `Target`.

```csharp
// Message catch (point-to-point):
new SchemataEventSubscription {
    SubscriptionId = $"flow:{process.CanonicalName}:{elementId}",
    EventType      = definition.Name,
    CorrelationKey = process.CanonicalName,
    Target         = process.CanonicalName,
    Name           = $"flow:{process.CanonicalName}:{elementId}",
    CanonicalName  = $"event-subscriptions/flow:{process.CanonicalName}:{elementId}",
};

// Signal catch (broadcast):
new SchemataEventSubscription {
    SubscriptionId = $"flow:{process.CanonicalName}:{elementId}",
    EventType      = definition.Name,
    CorrelationKey = null,
    Target         = process.CanonicalName,
    Name           = $"flow:{process.CanonicalName}:{elementId}",
    CanonicalName  = $"event-subscriptions/flow:{process.CanonicalName}:{elementId}",
};
```

The subscription id includes the waiting element id, so two catches in one process can wait for the
same message or signal name without overwriting each other. `EventType` carries the BPMN-level
`Message.Name` or `Signal.Name`, `Target` carries the process canonical name, and `CorrelationKey`
separates point-to-point messages from broadcast signals. Messages set `CorrelationKey` to the
process canonical name; signals leave it `null`.

## Same event consumed from multiple states

Commit `bc164b8a` fixed the state-machine layer that the event bridge depends on. The DSL creates a
new intermediate catch event for each `On(message)` call and copies the message name onto that catch.
Before the fix, `StateMachineValidator` rejected a process when two elements had the same `Name`, so
a process could not wait for the same `Message` from multiple activities or await states.

The validator now treats `FlowElement.Id` as the engine identity and allows duplicate display names.
`StateMachineEngine` also resumes by `StateId` only and derives `ProcessInstance.State` /
`WaitingAt` from the resolved element. User-visible effect: one process definition can consume the
same message or signal from multiple subscribed await states, including skip-step transitions built
from repeated `On(message)` catches. Each subscribed catch still gets its own subscription id through
the element id, and trigger matching still uses `Message.Name` / `Signal.Name`.

A breaking runtime detail comes with the fix: resumed `SchemataProcess` rows must carry `StateId`.
A row seeded only with the display `State` label is not enough for the state-machine engine to find
the current element.

## FlowEventHandler

`FlowEventHandler` implements `IEventHandler<IEvent>`. It reads `IEventDispatchContext`'s
`MatchedSubscriptions`, which the event bus fills before handler dispatch, and wakes Flow processes
from those matches:

- `CorrelationKey` set: call `IProcessRuntime.CorrelateMessageAsync(target, eventType, @event, ...)`.
- `CorrelationKey` null: call `IProcessRuntime.ThrowSignalAsync(eventType, @event, ...)`.

Signal throws are de-duplicated by event type within one handler call. If one dispatched event
matches several signal subscriptions with the same `EventType`, `FlowEventHandler` calls
`ThrowSignalAsync` once for that name; the runtime then broadcasts to every waiting matching process.
Message subscriptions are handled one by one because each message subscription targets one process
instance.

## ProcessEventLifecycleObserver

`ProcessEventLifecycleObserver` implements `IProcessLifecycleObserver` and publishes Flow lifecycle
notifications to `IEventBus` when the bus is available:

| Observer method | Published event | Payload |
| --- | --- | --- |
| `OnStartedAsync` | `ProcessStartedEvent` | Process canonical name, definition name, variables |
| `OnTransitionedAsync` | `TransitionMadeEvent` | Process canonical name, previous state, posterior state, waiting element id |
| `OnTerminatedAsync` | `ProcessCompletedEvent` | Process canonical name, definition name, variables |
| `OnFailedAsync` | `ProcessFailedEvent` | Process canonical name, definition name, error message |

The Flow runtime calls lifecycle observers after commits and logs observer exceptions. A failed
observer does not roll back a transition that already committed.

## Extension points

- Implement `IFlowTransitionAdvisor` and register via `TryAddEnumerable` to add subscription logic.
- Implement `IProcessLifecycleObserver` and register via `TryAddEnumerable` to publish additional
  lifecycle notifications after Flow commits.

## Caveats

- Subscription reconciliation joins the transition's unit of work. Subscription writes commit
  atomically with the process row; a transition rollback rolls subscription writes back together,
  so no orphan rows are left behind.
- `IEventHandler<IEvent>` is the generic handler. A more specific `IEventHandler<TEvent>` path may
  receive the same CLR event type before or instead of the generic handler, depending on the event
  bus implementation and registration order.
- Subscription ids use the `flow:{processCanonicalName}:{elementId}` format. Reserve the `flow:`
  prefix for this integration.
- Persisted or manually seeded processes must carry `StateId`; display `State` is not a resume key.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [Engine](engine.md)
- [Scheduling Integration](scheduling.md)
- [Event Overview](../event/overview.md)
