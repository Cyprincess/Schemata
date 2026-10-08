# Flow Event Integration

`Schemata.Flow.Event` bridges BPMN message and signal catches to the event bus. As a process
transitions, `FlowEventCatchHandler` keeps `IRepository<SchemataEventSubscription>` in sync with
the catches the instance is waiting on. When a matching event reaches the bus, `FlowEventHandler`
wakes waiting instances through the engine-neutral resource method handlers in
`Schemata.Flow.Foundation`. The same package also publishes process lifecycle
notifications through `ProcessEventLifecycleObserver`.

## Where the code lives

| Package                     | Key files                                                                                                                                                                                                                                                                                                                                                                                                                              |
| --------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Flow.Event`       | `Features/SchemataFlowEventFeature.cs`, `Events/ProcessStartedEvent.cs`, `Events/ProcessCompletedEvent.cs`, `Events/ProcessFailedEvent.cs`, `Events/TransitionMadeEvent.cs`, `Internal/FlowEventCatchHandler.cs`, `Internal/FlowEventHandler.cs`, `Internal/ProcessEventLifecycleObserver.cs`, `Extensions/FlowEventBuilderExtensions.cs` |
| `Schemata.Flow.Skeleton`    | `Runtime/IFlowCatchHandler.cs`, `Observers/FlowTransitionContext.cs`, `Runtime/FlowEventMatcher.cs`, `Runtime/IProcessLifecycleObserver.cs`                                                                                                                                                                                                                                                           |
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
= `490_300_000`. The feature declares `[DependsOn<SchemataFlowFeature>]` and
`[DependsOn<SchemataEventFeature>]`, so both are pulled in if missing. You still need a producer and
consumer transport on the event bus for events to move between publishers and consumers.

## What gets registered

`SchemataFlowEventFeature.ConfigureServices` registers three scoped services and four event type
aliases:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowCatchHandler, FlowEventCatchHandler>());
services.TryAddEnumerable(ServiceDescriptor.Scoped<IProcessLifecycleObserver, ProcessEventLifecycleObserver>());
services.TryAddScoped<IEventHandler<IEvent>, FlowEventHandler>();

services.Configure<EventTypeRegistryConfiguration>(options => {
    options.Registrations.Add((typeof(ProcessStartedEvent),    "schemata/flow/process.started"));
    options.Registrations.Add((typeof(ProcessCompletedEvent),  "schemata/flow/process.completed"));
    options.Registrations.Add((typeof(ProcessFailedEvent),     "schemata/flow/process.failed"));
    options.Registrations.Add((typeof(TransitionMadeEvent),    "schemata/flow/transition.made"));
});
```

`FlowEventCatchHandler` reconciles the subscription repository before a transition commits.
`ProcessEventLifecycleObserver` publishes process lifecycle events after persistence; the per-token
lifecycle observer path and its fork/join/cancel events were removed, so only process-level
notifications remain. `FlowEventHandler` is
the generic `IEvent` handler that wakes waiting instances when the bus dispatches a matched event.
`FlowEventMatcher` lives in `Schemata.Flow.Skeleton`, so both engines apply the same matching rule to
boundary events, event-based branches, and intermediate catches.

## FlowEventCatchHandler

`FlowEventCatchHandler` is an `IFlowCatchHandler`: it claims the `Message` and `Signal` catch kinds,
and its `ArmAsync` runs inside the transition's unit of work, before the process row is persisted,
reconciling `IRepository<SchemataEventSubscription>` against the new waiting state. Subscription
writes stage through `IResourceMutation<SchemataEventSubscription>` on `context.UnitOfWork`, so
subscription rows commit atomically with the process row and roll back together on any failure.

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

`FlowEventCatchHandler` writes `SchemataEventSubscription` rows through
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

The bridge forwards the materialized event instance as the request `Payload`; the matched
`EventType` becomes the message or signal name. Passing the instance (rather than a serialized JSON
string) keeps delivery working for processes that declare no payload type for the catch, since the
flow handlers bind only string payloads against the declared payload type. The handler-internal
request types (`CorrelateMessageRequest`, `ThrowSignalRequest`) live in
`Schemata.Flow.Skeleton.Models`.

- `CorrelationKey` set — open a scope, resolve `IRequestDispatcher` from it, and send
  `CorrelateMessageRequest` with `MessageName = sub.EventType`, the event instance as payload,
  `Token = sub.Token`, and the flow system principal.
- `CorrelationKey` null — open a scope, resolve `IRequestDispatcher` from it, and send
  `ThrowSignalRequest` with `SignalName = sub.EventType`, the event instance as payload,
  `Token = null`, and the flow system principal.

Signal throws are de-duplicated by event type within one handler call. If one dispatched event
matches several signal subscriptions with the same `EventType`, `FlowEventHandler` invokes the
`ThrowSignalHandler` once for that name; `FlowRunner.ThrowSignalAsync` then delivers to every waiting
process that declares the signal, each in its own unit of work, and returns one `SignalDeliveryResult`
per target. Message subscriptions are handled one by one because each message subscription targets one
process instance.

Signal broadcasts report per-target `SignalDeliveryResult` outcomes instead of throwing; the bridge
inspects them and rethrows the first faulted delivery's original exception, so a failed delivery
never reads as a successful publish. `NoLongerWaiting` — the target stopped waiting between
subscription match and delivery — is a legitimate race outcome and stays non-fatal. Message
deliveries propagate handler exceptions directly.

## ProcessEventLifecycleObserver

`ProcessEventLifecycleObserver` implements `IProcessLifecycleObserver`. It publishes Flow lifecycle
notifications to `IEventBus` when the bus is available:

| Observer method         | Interface                   | Published event         | Payload                                                                         |
| ----------------------- | --------------------------- | ----------------------- | ------------------------------------------------------------------------------- |
| `OnStartedAsync`        | `IProcessLifecycleObserver` | `ProcessStartedEvent`   | `ProcessCanonicalName`, `DefinitionName`, `DefinitionVersion` |
| `OnTransitionedAsync`   | `IProcessLifecycleObserver` | `TransitionMadeEvent`   | `ProcessCanonicalName`, `FromStateName`, `ToStateName`                              |
| `OnTerminatedAsync`     | `IProcessLifecycleObserver` | `ProcessCompletedEvent` | `ProcessCanonicalName`, `DefinitionName`, `DefinitionVersion` |
| `OnFailedAsync`         | `IProcessLifecycleObserver` | `ProcessFailedEvent`    | `ProcessCanonicalName`, `DefinitionName`, `DefinitionVersion`, `ErrorMessage` |

The Flow runtime calls lifecycle observers after commits and logs observer exceptions. A failed
observer does not roll back a transition that already committed.

## Extension points

- Implement `IFlowCatchHandler` and register via `TryAddEnumerable` to add subscription logic.
- Implement `IFlowTransitionAdvisor` and register via `TryAddEnumerable` to observe or reject a
  transition without owning a catch kind.
- Implement `IProcessLifecycleObserver` and register via `TryAddEnumerable` to publish additional
  process lifecycle notifications after Flow commits.

## Caveats

- Subscription reconciliation joins the transition's unit of work. Subscription writes commit
  atomically with the process row; a transition rollback rolls subscription writes back together,
  so no orphan rows are left behind.
- `IEventHandler<IEvent>` is the catch-all handler; the Flow bridge registers `FlowEventHandler`
  behind it. One publish composes the typed `IEventHandler<TEvent>` set for the event's runtime CLR
  type with the catch-all set into one candidate collection, deduplicated by instance reference, so
  an event with both an armed Flow subscription and a typed handler reaches both exactly once.
  Typed candidates run first in their own DI registration order, then catch-all candidates in
  theirs.
- Subscription ids use the `flow:{processCanonicalName}:{elementName}:{token|broadcast}` format.
  Reserve the `flow:` prefix for this integration.
- Persisted or manually seeded processes must carry `StateName`; display `State` is not a resume key.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [Engine](engine.md)
- [Scheduling Integration](scheduling.md)
- [Event Overview](../event/overview.md)
