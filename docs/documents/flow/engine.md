# Flow Engine

The `StateMachineEngine` is a single-token BPMN runtime that implements `IFlowRuntime`. It operates on a `ProcessDefinition` AST, traversing the graph using object references rather than string IDs, and tracks explicit waiting states via `ProcessInstance.WaitingAt`. `SchemataFlowFeature` registers it during `ConfigureServices` under the `SchemataConstants.FlowEngines.StateMachine` keyed-singleton slot.

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
| Intermediate catch event | Yes | Only after event-based gateway |
| Boundary event (Error, Timer, Message, Signal, interrupting) | Yes | |
| Boundary event (non-interrupting) | No | Requires multi-token |
| Exclusive gateway | Yes | |
| Event-based gateway (exclusive mode) | Yes | |
| Parallel gateway | No | Requires multi-token |
| Inclusive gateway | No | Requires multi-token |
| Task (all types) | Yes | |
| Sub-process / Call activity | No | |
| Multi-instance / Loop | No | |

## IFlowRuntime interface

```csharp
public interface IFlowRuntime
{
    string EngineName { get; }

    ValueTask<ProcessInstance> StartAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        CancellationToken ct = default);

    ValueTask<ProcessInstance> TriggerAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        IEventDefinition  trigger,
        object?           payload,
        CancellationToken ct = default);

    ValueTask<ProcessInstance> AdvanceAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        CancellationToken ct = default);
}
```

`StateMachineEngine.EngineName` returns `SchemataConstants.FlowEngines.StateMachine`.

## StartAsync

1. Locates the single `FlowEvent` with `Position == Start` in `Elements`.
2. Follows its single outgoing `SequenceFlow`.
3. Recursively resolves the target state via `ResolveTargetStateAsync`.
4. Returns a `ProcessInstance` with `StateId`, `State`, `WaitingAtId`, `WaitingAt` (if stopped at an `EventBasedGateway`), and `IsComplete`.

## TriggerAsync

Event triggers are matched in a priority order that prevents ambiguous matches:

1. **WaitingAt** — if `process.WaitingAtId` is set, only the `EventBasedGateway` branches at that element are checked. This prevents a stray trigger from matching a boundary event on the preceding activity.
2. **Boundary events** — if `WaitingAtId` is null, checks `FlowEvent`s with `Position == Boundary` and `AttachedTo == currentActivity`.
3. **Event-based gateway on current activity** — if the current activity has a single outgoing flow to an `EventBasedGateway`, checks its branches.

**Matching**: tries reference equality first (`ReferenceEquals`), then falls back to `IEventDefinition.Name` and type comparison. This allows the same definition object to be reused across branches while still accepting external triggers that arrive by name.

**On match**: follows the matched flow through the catch event's outgoing `SequenceFlow` and recursively resolves the target state.

## AdvanceAsync

1. If `WaitingAtId` is set, returns the instance unchanged — it must wait for an explicit trigger.
2. Locates the current element by `StateId` (then `State` as fallback) in `Elements`.
3. Follows outgoing `SequenceFlow`s:
   - Zero outgoing: terminal, no auto-advance.
   - One unconditional: follow it.
   - Multiple with conditions: evaluate in order; first `true` wins; fallback to `IsDefault` flow.
   - Target is `EventBasedGateway`: set `WaitingAtId`/`WaitingAt` and return.

## ResolveTargetStateAsync (recursive)

| Target type | StateId | WaitingAtId | IsComplete |
| --- | --- | --- | --- |
| `Activity` | `activity.Id` | `null` | `false` |
| `FlowEvent` with `Position == End` | `event.Id` | `null` | `true` |
| `FlowEvent` with `Position == IntermediateCatch` | `event.Id` | `event.Id` | `false` |
| `FlowEvent` with `Position == IntermediateThrow` | auto-follows outgoing, recurses | — | — |
| `EventBasedGateway` | `gateway.Id` | `gateway.Id` | `false` |
| `ExclusiveGateway` | evaluated, recurses on result | — | — |
| `ParallelGateway` or `InclusiveGateway` | throws `FailedPreconditionException` | — | — |

## WaitingAt semantics

`WaitingAtId` is the mechanism that distinguishes "at an Activity, ready to auto-advance" from "parked at an event, waiting for a trigger":

- When the token reaches an `EventBasedGateway` or `IntermediateCatchEvent`, `StateId` stores the element's ID and `WaitingAtId` stores the same ID.
- `AdvanceAsync` returns early when `WaitingAtId != null`.
- `TriggerAsync` uses `WaitingAtId` as the primary lookup key for matching.
- HTTP/gRPC response DTOs expose `WaitingAt` so API consumers can check whether an instance needs input.

## Extension points

- Implement `IFlowRuntime` and register it as a keyed singleton to provide an alternative engine (e.g., one that supports parallel gateways).
- Implement `IFlowEngineValidator` and register via `TryAddEnumerable` to add engine-specific validation rules.

## Design motivation

The single-token model covers the vast majority of real-world approval and order-processing workflows without the complexity of multi-token semantics. Keeping the engine stateless (all state lives in `SchemataProcess`) means the engine can be a singleton and process instances can be resumed on any node after a restart.

## Caveats

- `ParallelGateway` and `InclusiveGateway` are present in the AST but throw at runtime. The `StateMachineValidator` rejects them at registration time, so this should never be reached in production.
- Condition evaluation calls `ValueTask<bool>.GetAwaiter().GetResult()` internally because condition delegates compile to synchronous lambdas. Avoid async conditions.

## See also

- [AST Reference](ast.md)
- [Validator](validator.md)
- [State Machine](state-machine.md)
- [Runtime Services](runtime.md)
