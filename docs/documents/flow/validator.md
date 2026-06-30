# Flow Validator

`StateMachineValidator` checks that a `ProcessDefinition` uses only what the single-token engine
can execute. `StateMachineFlowEngineValidator` adapts it to the `IFlowEngineValidator` contract
and runs during registration for every process whose engine is `"statemachine"`. Most violations
throw `FailedPreconditionException`; the typed-payload consistency check throws
`InvalidOperationException`.

## Where the code lives

| Package                      | Key files                                                                                                                                               |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Flow.StateMachine` | `StateMachineValidator.cs`, `StateMachineFlowEngineValidator.cs`                                                                                        |
| `Schemata.Flow.Skeleton`     | `Runtime/IFlowEngineValidator.cs`, `Runtime/ProcedureTaskPayloadValidator.cs`, `Utilities/ProcessStructureValidator.cs`, `Utilities/FlowDiagnostics.cs` |

## IFlowEngineValidator

```csharp
public interface IFlowEngineValidator
{
    string EngineName { get; }
    void   Validate(ProcessDefinition definition);
}
```

`ProcessRegistry.RegisterAsync` calls `Validate` on every registered `IFlowEngineValidator` whose
`EngineName` matches the configuration's engine. `StateMachineFlowEngineValidator.EngineName`
returns `SchemataConstants.FlowEngines.StateMachine` (the string `"statemachine"`), and its
`Validate` delegates to the static `StateMachineValidator.Validate(definition)`.

## Validation rules

`StateMachineValidator.Validate` runs these checks in order. Failed-precondition checks emit
`FailedPreconditionException` with the matching `STATE_MACHINE_*` reason and the offending
element's `Name`. `RequiresBpmnEngine` rejections include the element's CLR type as a second
argument so callers can branch on `ErrorInfo.reason == STATE_MACHINE_REQUIRES_BPMN_ENGINE` to
surface "switch to `UseBpmn()`" guidance.

| Area                  | Rule                                                                                                                                                                          | Reason tag                                                                                                                                                                                                |
| --------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Start                 | Exactly one start event, with exactly one outgoing flow.                                                                                                                      | `STATE_MACHINE_REQUIRES_ONE_START_EVENT`, `STATE_MACHINE_START_EVENT_OUTGOING`                                                                                                                            |
| End                   | At least one end event.                                                                                                                                                       | `STATE_MACHINE_REQUIRES_END_EVENT`                                                                                                                                                                        |
| Typed payload         | Every `ProcedureTask<TPayload>` must be reachable only through catches whose payload type matches.                                                                            | (throws `InvalidOperationException`)                                                                                                                                                                      |
| Flows                 | Every flow has a non-null source and target, both present in `Elements`; end events have no outgoing flows.                                                                   | `STATE_MACHINE_FLOW_NO_SOURCE`, `STATE_MACHINE_FLOW_NO_TARGET`, `STATE_MACHINE_FLOW_UNKNOWN_SOURCE`, `STATE_MACHINE_FLOW_UNKNOWN_TARGET`, `STATE_MACHINE_END_EVENT_OUTGOING`                              |
| Gateways              | Only `ExclusiveGateway` and `EventBasedGateway` are allowed. Anything else throws `RequiresBpmnEngine`.                                                                       | `STATE_MACHINE_REQUIRES_BPMN_ENGINE`                                                                                                                                                                      |
| Event gateway         | `Parallel == false`; at least one outgoing flow; every outgoing flow targets an `IntermediateCatch` `FlowEvent`.                                                              | `STATE_MACHINE_REQUIRES_BPMN_ENGINE` (`EventBasedGateway[parallel=true]`), `STATE_MACHINE_EVENT_GATEWAY_NO_OUTGOING`, `STATE_MACHINE_EVENT_GATEWAY_TARGET`                                                |
| Exclusive gateway     | At least one outgoing flow.                                                                                                                                                   | `STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING`                                                                                                                                                             |
| Boundary events       | `AttachedTo` is non-null and present in the definition; `Interrupting == true`; exactly one outgoing flow.                                                                    | `STATE_MACHINE_BOUNDARY_UNATTACHED`, `STATE_MACHINE_BOUNDARY_UNKNOWN_ACTIVITY`, `STATE_MACHINE_REQUIRES_BPMN_ENGINE` (`FlowEvent[Boundary,non-interrupting]`), `STATE_MACHINE_BOUNDARY_OUTGOING_REQUIRED` |
| Intermediate catch    | At least one outgoing flow; reachable only from an `EventBasedGateway`.                                                                                                       | `STATE_MACHINE_CATCH_EVENT_NO_OUTGOING`, `STATE_MACHINE_CATCH_EVENT_GATEWAY_REQUIRED`                                                                                                                     |
| Activities            | At least one outgoing flow; at most one outgoing path type (no mixing direct activity, gateway, and end-event targets); at most one direct outgoing flow to another activity. | `STATE_MACHINE_ACTIVITY_NO_OUTGOING`, `STATE_MACHINE_ACTIVITY_MIXED_GATEWAY`, `STATE_MACHINE_ACTIVITY_MIXED_END`, `STATE_MACHINE_ACTIVITY_MULTIPLE_DIRECT`                                                |
| Pass-through none tasks | A `NoneTask` whose single outgoing flow targets an event-based gateway or an end event must not carry boundary events; the task never rests in Active, so the catches could never fire. | `STATE_MACHINE_NONE_TASK_BOUNDARY_UNREACHABLE`                                                                                                                                                            |
| Enter tasks           | Every inbound flow of an activity with an `OnEnter` chain must route through that chain. The DSL normalizes its own edges; this catches flows added around the builders. Also enforced by `BpmnValidator`. | `STATE_MACHINE_ENTER_TASK_BYPASSED`                                                                                                                                                                       |
| Sub-processes / loops | `SubProcess` and `CallActivity` are rejected via `RequiresBpmnEngine`. Any `LoopCharacteristics` is rejected via `RequiresBpmnEngine(activity, loopType)`.                    | `STATE_MACHINE_REQUIRES_BPMN_ENGINE`                                                                                                                                                                      |
| Reachability          | Every element is reachable from the start event. Boundary events count as reachable through their host activity.                                                              | `STATE_MACHINE_ELEMENT_UNREACHABLE`                                                                                                                                                                       |

Element names are validated first (`ProcessStructureValidator.ValidateElementNames`): every element
requires a non-empty, definition-unique name, since names are the canonical identity persisted on
tokens. The validator does not enforce paired signal/message payloads or single-event triggers;
those concerns live elsewhere.

`ProcedureTaskPayloadValidator` runs between end-events and flow-integrity. It walks every typed
`ProcedureTask<TPayload>`, collects the payload types on upstream `IntermediateCatch` events
carrying `Message` / `Signal` definitions, and throws `InvalidOperationException` if any task has
no matching typed catch or if its payload type does not match the catch payload type. The error
message lists every offending task by name.

## Reason tags

The full set of state-machine error reason tags lives in `SchemataResources.resx`:

```
STATE_MACHINE_REQUIRES_BPMN_ENGINE            STATE_MACHINE_NO_START_EVENT
STATE_MACHINE_START_EVENT_OUTGOING            STATE_MACHINE_REQUIRES_ONE_START_EVENT
STATE_MACHINE_REQUIRES_END_EVENT              STATE_MACHINE_INVALID_TRIGGER
STATE_MACHINE_UNKNOWN_CURRENT_STATE           STATE_MACHINE_GATEWAY_UNSUPPORTED
STATE_MACHINE_GATEWAY_KIND_UNSUPPORTED        STATE_MACHINE_CYCLIC_AUTO_FLOW
STATE_MACHINE_UNKNOWN_TARGET                  STATE_MACHINE_ACTIVITY_UNSUPPORTED
STATE_MACHINE_ACTIVITY_LOOP_UNSUPPORTED       STATE_MACHINE_ELEMENT_UNREACHABLE
STATE_MACHINE_FLOW_NO_SOURCE                  STATE_MACHINE_FLOW_NO_TARGET
STATE_MACHINE_FLOW_UNKNOWN_SOURCE             STATE_MACHINE_FLOW_UNKNOWN_TARGET
STATE_MACHINE_END_EVENT_OUTGOING              STATE_MACHINE_EVENT_GATEWAY_PARALLEL_UNSUPPORTED
STATE_MACHINE_EVENT_GATEWAY_NO_OUTGOING       STATE_MACHINE_EVENT_GATEWAY_TARGET
STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING   STATE_MACHINE_BOUNDARY_UNATTACHED
STATE_MACHINE_BOUNDARY_UNKNOWN_ACTIVITY       STATE_MACHINE_BOUNDARY_NON_INTERRUPTING
STATE_MACHINE_BOUNDARY_OUTGOING_REQUIRED      STATE_MACHINE_CANCEL_BOUNDARY_REQUIRES_TRANSACTION
STATE_MACHINE_TRANSACTION_REQUIRES_END_EVENT  STATE_MACHINE_CATCH_EVENT_NO_OUTGOING
STATE_MACHINE_CATCH_EVENT_GATEWAY_REQUIRED    STATE_MACHINE_ACTIVITY_NO_OUTGOING
STATE_MACHINE_ACTIVITY_MIXED_GATEWAY          STATE_MACHINE_ACTIVITY_MIXED_END
STATE_MACHINE_ACTIVITY_MULTIPLE_DIRECT        STATE_MACHINE_MULTI_INSTANCE_CARDINALITY_REQUIRED
STATE_MACHINE_MULTI_INSTANCE_EVENT_BEHAVIOR_UNSUPPORTED
STATE_MACHINE_CALL_ACTIVITY_CALLED_ELEMENT_REQUIRED
STATE_MACHINE_EVENT_SUBPROCESS_START_TRIGGER_REQUIRED
STATE_MACHINE_ESCALATION_NAME_REQUIRED
STATE_MACHINE_ELEMENT_NAME_REQUIRED            STATE_MACHINE_DUPLICATE_ELEMENT_NAME
STATE_MACHINE_NONE_TASK_BOUNDARY_UNREACHABLE   STATE_MACHINE_ENTER_TASK_BYPASSED
```

`RequiresBpmnEngine` emits `STATE_MACHINE_REQUIRES_BPMN_ENGINE` for everything the state machine
does not run: parallel, inclusive, and complex gateways, sub-processes, call activities, loop
characteristics (any kind), non-interrupting boundaries, and `EventBasedGateway.Parallel == true`.

## Usage

Validation runs automatically when a process is registered, whether at startup or at runtime:

```csharp
// Startup, via SchemataFlowBuilder:
flow.Use<ApprovalProcess>();

// Runtime, via IProcessRegistry:
await registry.RegisterAsync<ApprovalProcess>();
```

The static entry point validates a definition without the DI container, which is convenient in
unit tests:

```csharp
StateMachineValidator.Validate(new ApprovalProcess());   // throws on violation
```

## Extension points

- Implement `IFlowEngineValidator` and register via `TryAddEnumerable`. A custom validator runs
  only for the engine named in its `EngineName`.
- Implement `IConditionExpression` to plug a custom guard evaluator into `When(...)`.

## Design rationale

Validating at registration, which runs at startup for `flow.Use<T>()`, means an invalid
definition fails fast at startup, before any request reaches the engine. Modeling errors surface
in the build/boot loop instead of at runtime on a specific path.

## Caveats

- The validator checks structure and engine applicability only. It does not verify that guard
  expressions are sound, that the binding key a `When<T>` reads is configured, or that an
  activity's `ProcedureTask` body respects the flow transaction.
- `StateMachineValidator.Validate` is static; it can be called without DI.

## See also

- [Engine](engine.md)
- [AST Reference](ast.md)
- [DSL Reference](dsl.md)
- [State Machine](state-machine.md)
