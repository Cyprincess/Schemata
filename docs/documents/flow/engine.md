# State Machine Engine

> **Source:** `Schemata.Flow.StateMachine/StateMachineEngine.cs`

The `StateMachineEngine` is a single-token BPMN runtime that implements `IFlowRuntime`. It operates on a `ProcessDefinition` AST, traversing the graph using object references rather than string IDs, and tracks explicit waiting states via `ProcessInstance.WaitingAt`.

## Supported BPMN Subset

| Element | Supported | Notes |
|---------|:---:|-------|
| Start Event (None, Message, Signal, Timer, Conditional) | Y | |
| End Event (None, Terminate) | Y | |
| Intermediate Catch Event | Y | Only after Event-Based Gateway |
| Boundary Event — Error, Timer, Message, Signal (interrupting) | Y | |
| Boundary Event — non-interrupting | **N** | Requires multi-token |
| Exclusive Gateway | Y | |
| Event-Based Gateway (exclusive mode) | Y | |
| Parallel Gateway | **N** | Requires multi-token |
| Inclusive Gateway | **N** | Requires multi-token |
| Task (all types) | Y | |
| Sub-Process / Call Activity | **N** | |
| Multi-Instance / Loop | **N** | |

## IFlowRuntime Interface

```csharp
public interface IFlowRuntime
{
    string EngineName { get; }

    ValueTask<ProcessInstance> StartAsync(
        ProcessDefinition definition, SchemataProcess process,
        CancellationToken ct = default);

    ValueTask<ProcessInstance> TriggerAsync(
        ProcessDefinition definition, SchemataProcess process,
        IEventDefinition trigger, object? payload,
        CancellationToken ct = default);

    ValueTask<ProcessInstance> AdvanceAsync(
        ProcessDefinition definition, SchemataProcess process,
        CancellationToken ct = default);
}
```

## StartAsync

1. Locates the single `FlowEvent` with `Position == Start` in `Elements`.
2. Follows its single outgoing `SequenceFlow`.
3. Recursively resolves the target state via `ResolveTargetState`.
4. Returns a `ProcessInstance` with `State`, `WaitingAt` (if stopped at an EventBasedGateway), and `IsComplete`.

## TriggerAsync

Event triggers are matched in a **priority order** that prevents ambiguous matches:

1. **WaitingAt** — If `process.WaitingAt` is set, only the EventBasedGateway branches at that element are checked. This prevents a stray trigger from matching a boundary event on the preceding activity.
2. **Boundary events** — If `WaitingAt` is null, checks `FlowEvent`s with `Position == Boundary` and `AttachedTo == currentActivity`.
3. **Event-Based Gateway on current Activity** — If the current activity has a single outgoing flow to an `EventBasedGateway`, checks its branches.

**Matching**: tries reference equality first (`ReferenceEquals`), then falls back to `IEventDefinition.Name` comparison. This allows the same definition object to be reused across branches while still accepting external triggers that arrive by name.

**On match**: follows the matched flow through the catch event's outgoing `SequenceFlow` and recursively resolves the target state.

## AdvanceAsync

1. If `WaitingAt` is set, returns the instance unchanged — it must wait for an explicit trigger.
2. Locates the current element by `Name` match against `Elements`.
3. Follows outgoing `SequenceFlow`s:
   - Zero outgoing → terminal (no auto-advance).
   - One unconditional → follow it.
   - Multiple with conditions → evaluate in order; first `true` wins; fallback to `IsDefault` flow.
   - Target is `EventBasedGateway` → set `WaitingAt` and return.

**Condition evaluation**: Conditions implement `IConditionExpression.Evaluate(FlowConditionContext)` which returns `ValueTask<bool>`. The engine calls `.GetAwaiter().GetResult()` because condition delegates compile to synchronous lambdas that never actually suspend.

## ResolveTargetState (Recursive)

| Target type | State set to | WaitingAt set to | IsComplete |
|---|---|---|---|
| `Activity` | `activity.Name` | `null` | `false` |
| `FlowEvent` with `Position == End` | `event.Name` | `null` | `true` |
| `FlowEvent` with `Position == IntermediateCatch` | `event.Name` | `event.Name` | `false` |
| `FlowEvent` with `Position == IntermediateThrow` | auto-follows outgoing, recurses | — | — |
| `EventBasedGateway` | `gateway.Name` | `gateway.Name` | `false` |
| `ExclusiveGateway` | evaluated, recurses on result | — | — |
| `ParallelGateway` or `InclusiveGateway` | throws | — | — |

## WaitingAt Semantics

`WaitingAt` is the mechanism that distinguishes "at an Activity, ready to auto-advance" from "parked at an event, waiting for a trigger":

- When the token reaches an `EventBasedGateway` or `IntermediateCatchEvent`, `State` stores the preceding Activity's name (the one the user last acted on), and `WaitingAt` stores the gateway or catch event's name.
- `AdvanceAsync` returns early when `WaitingAt != null` — the instance must be triggered.
- `TriggerAsync` uses `WaitingAt` as the primary lookup key for matching.
- HTTP/gRPC response DTOs expose `WaitingAt` so API consumers can check whether an instance needs input.
