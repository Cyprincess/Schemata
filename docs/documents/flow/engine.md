# Flow Engine

`StateMachineEngine` is the single-token BPMN runtime that implements `IFlowRuntime`. It traverses a
`ProcessDefinition` AST using object references, tracks explicit waiting states through
`ProcessInstance.WaitingAt`, and holds no state of its own — every call takes the current
`SchemataProcess` and returns a fresh `ProcessInstance`. `SchemataFlowFeature` registers it as the
keyed singleton under `SchemataConstants.FlowEngines.StateMachine`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.StateMachine` | `StateMachineEngine.cs`, `StateMachineFlowEngineValidator.cs`, `StateMachineValidator.cs` |
| `Schemata.Flow.Skeleton` | `Runtime/IFlowRuntime.cs`, `Runtime/IFlowEngineValidator.cs`, `Models/ProcessInstance.cs` |

## Supported BPMN subset

| Element | Supported | Notes |
| --- | :---: | --- |
| Start event (None, Message, Signal, Timer, Conditional) | Yes | |
| End event (None, Terminate) | Yes | |
| Intermediate catch event | Yes | Reachable only from an event-based gateway |
| Boundary event (Error, Timer, Message, Signal, Conditional, Escalation), interrupting | Yes | |
| Boundary event, non-interrupting | No | |
| Exclusive gateway | Yes | |
| Event-based gateway, exclusive mode (`Parallel == false`) | Yes | |
| Parallel / Inclusive / Complex gateway | No | |
| Task (all eight types) | Yes | All treated identically |
| Sub-process / Call activity | No | |
| Multi-instance / Loop characteristics | No | |

Unsupported elements are rejected by the [validator](validator.md) at registration. The engine also
guards against them at runtime, throwing `FailedPreconditionException` if one is reached.

## IFlowRuntime

```csharp
public interface IFlowRuntime
{
    string EngineName { get; }

    ValueTask<ProcessInstance> StartAsync(
        ProcessDefinition definition, SchemataProcess process, CancellationToken ct = default);

    ValueTask<ProcessInstance> TriggerAsync(
        ProcessDefinition definition, SchemataProcess process,
        IEventDefinition trigger, object? payload, CancellationToken ct = default);

    ValueTask<ProcessInstance> AdvanceAsync(
        ProcessDefinition definition, SchemataProcess process, CancellationToken ct = default);
}
```

`StateMachineEngine.EngineName` returns `SchemataConstants.FlowEngines.StateMachine` (`"statemachine"`).

## StartAsync

1. Finds the single `FlowEvent` with `Position == Start`. No start event, or a start event without
   exactly one outgoing flow, throws `FailedPreconditionException`.
2. Deserializes `process.Variables` into the new instance.
3. Resolves the target of the start event's outgoing flow into the initial state.

## TriggerAsync

`TriggerAsync` merges the payload into the instance variables (a `Dictionary<string, object?>` is
merged key by key; any other non-null payload is stored under `"payload"`), then resolves the
trigger against the current position:

1. **Waiting at an element** (`WaitingAtId` set): if the waiting element is an `EventBasedGateway`,
   or an intermediate catch reached from one, the gateway's branches are matched against the trigger.
2. **At an activity** (`WaitingAtId` null): boundary events attached to the current activity are
   matched first; failing that, the activity's outgoing event-based gateway is matched.

A trigger that matches nothing valid from the current state throws `InvalidArgumentException`.

**Matching** tries reference equality first (`ReferenceEquals(evt.Definition, trigger)`), then falls
back to name and type (`evt.Definition.Name == trigger.Name && same runtime type`). Reference
equality lets the same definition object serve every branch; the name/type fallback accepts triggers
that arrive from outside by name, such as an `ErrorDefinition` reconstructed from a thrown exception.

## AdvanceAsync

1. If `WaitingAtId` is set, the instance is parked at an event — returns it unchanged.
2. Locates the current element (by `StateId`, then `State`). A missing element throws
   `NotFoundException`.
3. Resolves the auto-flow from that element. Zero viable flows leaves the instance where it is.

## Target resolution

Resolving a flow's target walks forward until it reaches a stable state. The outcome per target type:

| Target | StateId / State | WaitingAt | IsComplete |
| --- | --- | --- | --- |
| `Activity` | the activity | cleared | `false` |
| `FlowEvent` (`End`) | the event | cleared | `true` |
| `FlowEvent` (`IntermediateCatch`) | the event | the event | `false` |
| `EventBasedGateway` | the gateway | the gateway | `false` |
| `ExclusiveGateway` | evaluate guards, recurse on the chosen flow | — | — |
| `FlowEvent` (other) | follow a single outgoing flow if present, else stop | — | — |
| `ParallelGateway` / `InclusiveGateway` / `ComplexGateway` | throws `FailedPreconditionException` | — | — |

A pure gateway cycle (a gateway whose flows loop back without reaching a stable element) is detected
and throws `FailedPreconditionException` rather than recursing forever.

## Guard evaluation

Guarded flows (`SequenceFlow.Condition`) are evaluated by `await`ing `IConditionExpression.Evaluate`
against a `FlowConditionContext`. Evaluation is fully asynchronous. At a gateway with multiple
flows, the engine evaluates guards in order and takes the first that returns `true`; if none match,
it takes the flow with no condition (the default).

## WaitingAt semantics

`WaitingAtId` is what separates "ready to auto-advance" from "waiting for a trigger":

- Reaching an `EventBasedGateway` or an `IntermediateCatch` event sets both `StateId` and
  `WaitingAtId` to that element.
- `AdvanceAsync` returns early while `WaitingAtId` is set.
- `TriggerAsync` uses `WaitingAtId` as its primary lookup key.

## Extension points

- Implement `IFlowRuntime` and register it as a keyed singleton to provide an engine that executes
  more of the AST (parallel flow, sub-processes, compensation).
- Implement `IFlowEngineValidator` and register via `TryAddEnumerable` to add engine-specific
  validation.

## Design rationale

The single-token model covers the bulk of approval and order-processing workflows without
multi-token bookkeeping. Because the engine is stateless and all state lives on `SchemataProcess`,
it is a singleton and an instance can be resumed on any node after a restart.

## Caveats

- Unsupported gateways are present in the AST and throw at runtime, but the validator rejects them at
  registration, so a started instance never reaches that throw.
- A trigger that does not match the waiting element raises `InvalidArgumentException`; callers should
  surface this as a client error rather than an internal fault.

## See also

- [AST Reference](ast.md)
- [Validator](validator.md)
- [State Machine](state-machine.md)
- [Runtime Services](runtime.md)
