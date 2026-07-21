# Flow Event Integration

`Schemata.Flow.Event` bridges BPMN message and signal catches to the event bus. As a process
transitions, `AdviceTransitionEvent` keeps `IRepository<SchemataEventSubscription>` in sync with
the catches the instance is waiting on. When a matching event reaches the bus, `FlowEventHandler`
wakes waiting instances through the engine-neutral resource method handlers in
`Schemata.Flow.Foundation`. The same package also publishes process lifecycle
notifications through `ProcessEventLifecycleObserver`.

## Where the code lives

| Package                     | Key files                                                                                                                                                                                                                                                                                                                                                                                                                              |
| --------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Flow.Event`       | `Features/SchemataFlowEventFeature.cs`, `Events/ProcessStartedEvent.cs`, `Events/ProcessCompletedEvent.cs`, `Events/ProcessFailedEvent.cs`, `Events/TransitionMadeEvent.cs`, `Internal/AdviceTransitionEvent.cs`, `Internal/FlowEventHandler.cs`, `Internal/ProcessEventLifecycleObserver.cs`, `Extensions/FlowEventBuilderExtensions.cs` |
| `Schemata.Flow.Skeleton`    | `Observers/IFlowTransitionAdvisor.cs`, `Observers/FlowTransitionContext.cs`, `Runtime/IProcessLifecycleObserver.cs`                                                                                                                                                                                                                                                                                      |
| `Schemata.Event.Skeleton`   | `Entities/SchemataEventSubscription.cs`, `IEventHandler.cs`, `IEventDispatchContext.cs`                                                                                                                                                                                                                                                                                                                                                |
| `Schemata.Event.Foundation` | `SchemataEventSubscriptionExtensions.cs`                                                                                                                                                                                                                                                                                                                                                                                               |

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
services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionAdvisor, AdviceTransitionEvent>());
services.TryAddEnumerable(ServiceDescriptor.Scoped<IProcessLifecycleObserver, ProcessEventLifecycleObserver>());
services.TryAddScoped<IEventHandler<IEvent>, FlowEventHandler>();

services.Configure<EventTypeRegistryConfiguration>(options => {
    options.Registrations.Add((typeof(ProcessStartedEvent),    "schemata/flow/process.started"));
    options.Registrations.Add((typeof(ProcessCompletedEvent),  "schemata/flow/process.completed"));
    options.Registrations.Add((typeof(ProcessFailedEvent),     "schemata/flow/process.failed"));
    options.Registrations.Add((typeof(TransitionMadeEvent),    "schemata/flow/transition.made"));
});
```

`AdviceTransitionEvent` reconciles the subscription repository before a transition commits.
`ProcessEventLifecycleObserver` publishes process lifecycle events after persistence; the per-token
lifecycle observer path and its fork/join/cancel events were removed, so only process-level
notifications remain. `FlowEventHandler` is
the generic `IEvent` handler that wakes waiting instances when the bus dispatches a matched event.

## AdviceTransitionEvent

`AdviceTransitionEvent` is an `IFlowTransitionAdvisor` (`IAdvisor<FlowTransitionContext>`). Its
`AdviseAsync` runs inside the transition's unit of work, before the process row is persisted, and
reconciles `IRepository<SchemataEventSubscription>` against the new waiting state. It enlists the
subscription repository in `context.UnitOfWork` via `IRepository.Join`, so subscription writes commit
atomically with the process row and roll back together on any failure. Returns `AdviseResult.Continue`.

### Subscription lifecycle

1. **Remove old subscriptions** when `PreviousWaitingAtName` is set and differs from the new
   `WaitingAtName`. The advisor resolves the previous element, looks up each subscription by
   `SubscriptionId`, and removes the matching row through `IRepository.RemoveAsync`. An event-based
   gateway removes the subscription for every outgoing intermediate catch.
2. **Skip the add** when the instance is complete or the new `WaitingAtName` is empty.
3. **Add new subscriptions** when the new waiting element is an intermediate catch with a definition,
   or an event-based gateway. The advisor upserts each subscription by `SubscriptionId`
   (`IRepository.FirstOrDefaultAsync` then `AddAsync` or `UpdateAsync`). A gateway adds one
   subscription per outgoing intermediate catch.

The advisor walks `ProcessDefinition.AllElements`, so intermediate catches, event-based gateway
branches, and boundary message/signal catches attached to the host activity of an active token each
get a subscription row. Boundary subscriptions follow the host activity rather than a waiting
element: when a token parks on or leaves an activity, its boundary catches are armed or disarmed
alongside the waiting-element subscriptions.

### Subscription format

`AdviceTransitionEvent` writes `SchemataEventSubscription` rows through
`IRepository<SchemataEventSubscription>`. Each row carries `SubscriptionId`, `EventType`,
`CorrelationKey`, `Target`, and `Token`.

```csharp
// Message catch (point-to-point, token-scoped):
new SchemataEventSubscription {
    SubscriptionId = $"flow:{process.CanonicalName}:{elementName}:{token.CanonicalName}",
    EventType      = definition.Name,
    CorrelationKey = process.CanonicalName,
    Target         = process.CanonicalName,
    Token          = token.CanonicalName,
};

// Signal catch (broadcast):
new SchemataEventSubscription {
    SubscriptionId = $"flow:{process.CanonicalName}:{elementName}:broadcast",
    EventType      = definition.Name,
    CorrelationKey = null,
    Target         = process.CanonicalName,
    Token          = null,
};
```

The subscription id is `flow:{process}:{element}:{token|broadcast}`: message rows key on the armed
token's canonical name, so two tokens parked on the same message name correlate independently;
signal rows carry the `broadcast` segment and a null `Token`, so one row serves the whole process.
`EventType` carries the BPMN-level `Message.Name` or `Signal.Name`, `Target` carries the process
canonical name, and `CorrelationKey` separates point-to-point messages from broadcast signals.

The DSL emits one intermediate catch event per `On(message)` call, and the gateway-scoped catch name
(`Catch_{gateway}_{eventDefinition}`) keeps each subscription distinct.

## FlowEventHandler

`FlowEventHandler` implements `IEventHandler<IEvent>`. It reads `IEventDispatchContext`'s
`MatchedSubscriptions`, which the event bus fills before handler dispatch, and wakes waiting
processes by invoking the engine-neutral resource method handlers in `Schemata.Flow.Foundation`
within a fresh DI scope per call. The handlers in turn call `FlowRunner.CorrelateAsync` or
`FlowRunner.ThrowSignalAsync`.

The bridge serializes the inbound `IEvent` to JSON (`JsonSerializer.Serialize(@event,
@event.GetType(), SchemataJson.Default)`) and forwards it as the request `Payload`; the matched
`EventType` becomes the message or signal name. The handler-internal request types
(`CorrelateMessageRequest`, `ThrowSignalRequest`) live in
`Schemata.Flow.Skeleton.Models`.

- `CorrelationKey` set — open a scope, resolve `ProcessPersistence`, load the process via
  `persistence.FindAsync`, resolve `CorrelateMessageHandler` from the scope, and invoke it with
  `MessageName = sub.EventType`, `Payload = <serialized event>`, `Token = sub.Token`, the loaded
  process as entity, and `null` principal.
- `CorrelationKey` null — open a scope, resolve `ThrowSignalHandler` from the scope, and invoke it
  with `SignalName = sub.EventType`, `Payload = <serialized event>`, `Token = null`, `null` entity,
  and `null` principal.

Signal throws are de-duplicated by event type within one handler call. If one dispatched event
matches several signal subscriptions with the same `EventType`, `FlowEventHandler` invokes the
`ThrowSignalHandler` once for that name; `FlowRunner.ThrowSignalAsync` then iterates every waiting
process and triggers the matched token(s). Message subscriptions are handled one by one because each
message subscription targets one process instance.

## ProcessEventLifecycleObserver

`ProcessEventLifecycleObserver` implements `IProcessLifecycleObserver`. It publishes Flow lifecycle
notifications to `IEventBus` when the bus is available:

| Observer method         | Interface                   | Published event         | Payload                                                                         |
| ----------------------- | --------------------------- | ----------------------- | ------------------------------------------------------------------------------- |
| `OnStartedAsync`        | `IProcessLifecycleObserver` | `ProcessStartedEvent`   | `ProcessCanonicalName`, `DefinitionName`                                        |
| `OnTransitionedAsync`   | `IProcessLifecycleObserver` | `TransitionMadeEvent`   | `ProcessCanonicalName`, `FromStateName`, `ToStateName`                              |
| `OnTerminatedAsync`     | `IProcessLifecycleObserver` | `ProcessCompletedEvent` | `ProcessCanonicalName`, `DefinitionName`                                            |
| `OnFailedAsync`         | `IProcessLifecycleObserver` | `ProcessFailedEvent`    | `ProcessCanonicalName`, `DefinitionName`, `ErrorMessage`                            |

The Flow runtime calls lifecycle observers after commits and logs observer exceptions. A failed
observer does not roll back a transition that already committed.

## Extension points

- Implement `IFlowTransitionAdvisor` and register via `TryAddEnumerable` to add subscription logic.
- Implement `IProcessLifecycleObserver` and register via `TryAddEnumerable` to publish additional
  process lifecycle notifications after Flow commits.

## Caveats

- Subscription reconciliation joins the transition's unit of work. Subscription writes commit
  atomically with the process row; a transition rollback rolls subscription writes back together,
  so no orphan rows are left behind.
- `IEventHandler<IEvent>` is the generic handler. The bus dispatches by CLR type, and the order in
  which handlers see a given event depends on the bus implementation and any registered
  `IEventHandler<TEvent>` for a more specific CLR type.
- Subscription ids use the `flow:{processCanonicalName}:{elementName}:{token|broadcast}` format.
  Reserve the `flow:` prefix for this integration.
- Persisted or manually seeded processes must carry `StateName`; display `State` is not a resume key.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [Engine](engine.md)
- [Scheduling Integration](scheduling.md)
- [Event Overview](../event/overview.md)
