# Flow Engine

`StateMachineEngine` is the single-token BPMN runtime that implements `IFlowRuntime`. It traverses a
`ProcessDefinition` AST using object references, tracks explicit waiting states through
`SchemataProcessToken.WaitingAtName`, and holds no state of its own. Every call takes the current
`SchemataProcess` plus token rows and returns a `ProcessSnapshot`. `SchemataFlowFeature` registers it
as the keyed singleton under `SchemataConstants.FlowEngines.StateMachine`.

## Where the code lives

| Package                      | Key files                                                                                                            |
| ---------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Flow.StateMachine` | `StateMachineEngine.cs`, `StateMachineFlowEngineValidator.cs`, `StateMachineValidator.cs`                            |
| `Schemata.Flow.Skeleton`     | `Runtime/IFlowRuntime.cs`, `Runtime/IFlowEngineValidator.cs`, `Runtime/FlowResolver.cs`, `Models/ProcessSnapshot.cs` |

## Supported BPMN subset

| Element                                                                               | Supported | Notes                                                                                                    |
| ------------------------------------------------------------------------------------- | :-------: | -------------------------------------------------------------------------------------------------------- |
| Start event (None, Message, Signal, Timer, Conditional)                               |    Yes    | One start, single outgoing flow enforced by the validator                                                |
| End event (None, Terminate)                                                           |    Yes    | Other end definitions route through the BPMN engine                                                      |
| Intermediate catch event                                                              |    Yes    | Reachable only from an event-based gateway                                                               |
| Boundary event (Error, Timer, Message, Signal, Conditional, Escalation), interrupting |    Yes    | Resolved by `FlowResolver.ResolveBoundaryEventFlow`                                                      |
| Boundary event, non-interrupting                                                      |    No     | The validator rejects non-interrupting boundaries (`!boundary.Interrupting` throws `RequiresBpmnEngine`) |
| Exclusive gateway                                                                     |    Yes    | First-match guard wins                                                                                   |
| Event-based gateway, exclusive mode (`Parallel == false`)                             |    Yes    | One matching branch is routed through the existing token                                                 |
| Parallel / Inclusive gateway                                                          |    No     | Routed through `FlowDiagnostics.RequiresBpmnEngine` at runtime                                           |
| Complex gateway                                                                       |    No     | AST shape accepted, engine execution throws `RequiresBpmnEngine`                                         |
| Task (all eight types)                                                                |    Yes    | The engine treats every `Activity` subtype identically                                                   |
| Sub-process / Call activity                                                           |    No     | Rejected by the validator                                                                                |
| Multi-instance / Loop characteristics                                                 |    No     | Rejected by the validator                                                                                |

Unsupported elements are rejected by the [validator](validator.md) at registration. The engine also
guards against them at runtime, throwing `FailedPreconditionException` if one is reached.

## IFlowRuntime

```csharp
public interface IFlowRuntime
{
    string EngineName { get; }

    ValueTask<ProcessSnapshot> StartAsync(
        ProcessDefinition    definition,
        SchemataProcess      process,
        FlowExecutionContext context,
        CancellationToken    ct = default
    );

    ValueTask<ProcessSnapshot> TriggerAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        IEventDefinition                    trigger,
        object?                             payload,
        string?                             tokenName = null,
        CancellationToken                   ct        = default
    );

    ValueTask<ProcessSnapshot> AdvanceAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        string?                             tokenName = null,
        CancellationToken                   ct        = default
    );

    ValueTask<IReadOnlyList<string>> FindTriggerTargetsAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        IEventDefinition                    trigger,
        CancellationToken                   ct = default
    );
}
```

`StateMachineEngine.EngineName` returns `SchemataConstants.FlowEngines.StateMachine` (`"statemachine"`).

Engines never load or persist state. Each method receives the current process row plus the live
token set and returns a `ProcessSnapshot` that the runtime persists under its own unit of work.

## StartAsync

1. Finds the single `FlowEvent` with `Position == Start`. No start event, or a start event without
   exactly one outgoing flow, throws `FailedPreconditionException`.
2. Creates a root token and resolves the target of the start event's outgoing flow into the
   initial state.

## TriggerAsync

`TriggerAsync` receives a typed message or signal payload from the caller, then resolves the trigger
against the current token position:

1. **Waiting at an element** (`WaitingAtName` set): if the waiting element is an `EventBasedGateway`,
   or an intermediate catch reached from one, the gateway's branches are matched against the trigger.
2. **At an activity** (`WaitingAtName` null): boundary events attached to the current activity are
   matched. A none task that awaits a gateway never rests in this position — it parks at the
   gateway on arrival — so its catches are always consumable through case 1.

A trigger that matches nothing valid from the current state throws `InvalidArgumentException`.

**Matching** tries reference equality first (`ReferenceEquals(evt.Definition, trigger)`), then falls
back to name and type (`evt.Definition.Name == trigger.Name && same runtime type`). Reference
equality lets the same definition object serve every branch; the name/type fallback accepts triggers
that arrive from outside by name, such as an `ErrorDefinition` reconstructed from a thrown exception.

## AdvanceAsync

1. If `WaitingAtName` is set, the instance is parked at an event, returns it unchanged.
2. Locates the current element (by `StateName`). A missing element throws `FailedPreconditionException`.
3. Resolves the auto-flow from that element. Zero viable flows leaves the instance where it is.
4. When the resolved hop parks at an event-based gateway, `StateName` keeps the completed
   activity's name; the gateway name surfaces only on `WaitingAtName`.

## FindTriggerTargetsAsync

Returns the canonical names of waiting tokens that can consume the supplied trigger. The
state-machine engine either returns the single live token or an empty list. The BPMN engine uses the
same call to enumerate every ready token before dispatching one or more `TriggerAsync` invocations
(see [Runtime Services](runtime.md) and [BPMN Engine](bpmn-engine.md)).

## Target resolution

Resolving a flow's target walks forward until it reaches a stable state. The outcome per target type:

| Target                                                    | StateName / State                                                             | WaitingAtName       | IsComplete          |
| --------------------------------------------------------- | ----------------------------------------------------------------------------- | ------------------- | ------------------- |
| `NoneTask` (single outgoing flow to an event-based gateway) | the task name                                                               | the gateway name    | `false`             |
| `NoneTask` (single outgoing flow to an end event)         | the task name                                                                 | cleared             | `true`              |
| `Activity` (any other shape)                              | the activity name                                                             | cleared             | `false`             |
| `FlowEvent` (`End`)                                       | the event name                                                                | cleared             | `true`              |
| `FlowEvent` (`IntermediateCatch`)                         | the event name                                                                | the event name      | `false`             |
| `EventBasedGateway`                                       | the gateway name                                                              | the gateway name    | `false`             |
| `ExclusiveGateway`                                        | evaluate guards, recurse on the chosen flow                                   | same as chosen flow | same as chosen flow |
| `FlowEvent` (other)                                       | follow a single outgoing flow if present, else stop                           | same as target      | same as target      |
| `ParallelGateway` / `InclusiveGateway` / `ComplexGateway` | throws `FailedPreconditionException` via `FlowDiagnostics.RequiresBpmnEngine` | n/a                 | n/a                 |

A pure gateway cycle (a gateway whose flows loop back without reaching a stable element) is detected
and throws `FailedPreconditionException` rather than recursing forever.

## Guard evaluation

Guarded flows (`SequenceFlow.Condition`) are evaluated by `await`ing `IConditionExpression.Evaluate`
against a `FlowConditionContext`. Evaluation is fully asynchronous. At a gateway with multiple
flows, the engine evaluates guards in order and takes the first that returns `true`; if none match,
it takes the flow with no condition (the default).

## WaitingAt semantics

`WaitingAtName` is what separates "ready to auto-advance" from "waiting for a trigger":

- Reaching an `IntermediateCatch` event sets both `StateName` and `WaitingAtName` to the event name.
- Parking at an `EventBasedGateway` sets `WaitingAtName` to the gateway name. Arriving from an
  activity (an explicit advance or a none-task pass-through) keeps the business state on
  `StateName`; only a gateway with no preceding activity (`Start().Await(...)`) surfaces the
  gateway name as `StateName`.
- `AdvanceAsync` returns early while `WaitingAtName` is set.
- `TriggerAsync` uses `WaitingAtName` as its primary lookup key.

Both engines keep this contract. The BPMN engine also parks with the arriving token's previous
business node on `StateName` and the gateway name on `WaitingAtName`, so trigger dispatch and
source projection read the same values regardless of engine. The shared exception is a process
that starts directly into an event-based gateway (`Start().Await(...)` on this engine, the
`StartIntoEventBased` path on the BPMN engine): with no preceding activity, `StateName` holds the
gateway name.

A `NoneTask` whose single outgoing flow targets an `EventBasedGateway` parks at that gateway on
arrival, and one whose single outgoing flow targets an end event completes on arrival. A
message-driven state machine modeled with none tasks therefore runs start → correlate → correlate,
with no explicit complete between hops; `UserTask` and the other task types keep the
explicit-complete wait state. Boundary catches on a pass-through none task could never arm, so the
validator rejects that combination.

## Extension points

- Implement `IFlowRuntime` and register it as a keyed singleton to provide an engine that executes
  more of the AST (parallel flow, sub-processes, compensation).
- Implement `IFlowEngineValidator` and register via `TryAddEnumerable` to add engine-specific
  validation.

## Design rationale

The single-token model covers the bulk of approval and order-processing workflows without
multi-token bookkeeping. Because the engine is stateless and all state lives on `SchemataProcess`,
it is a singleton. Because element names are deterministic per definition rebuild, a process
persisted by one host resumes on any other host or after a restart: the rebuilt definition produces
identical element names, so `StateName` and `WaitingAtName` resolve to the same elements.

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
- [BPMN Engine](bpmn-engine.md)
