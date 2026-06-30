# State Machine Engine

`Schemata.Flow.StateMachine` is the default execution engine for Flow. `UseStateMachine()` on the
flow builder activates `SchemataFlowStateMachineFeature`, which registers the engine and its
validator under the `"statemachine"` key. Every process registered through
`SchemataFlowBuilder.Use<TProcess>()` with no explicit engine runs on this key.

## Where the code lives

| Package                      | Key files                                                                                                                                                                                   |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Flow.StateMachine` | `StateMachineEngine.cs`, `StateMachineFlowEngineValidator.cs`, `StateMachineValidator.cs`, `Features/SchemataFlowStateMachineFeature.cs`, `Extensions/FlowStateMachineBuilderExtensions.cs` |

## How it gets registered

`SchemataFlowStateMachineFeature` carries `[DependsOn<SchemataFlowFeature>]` and runs:

```csharp
services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(SchemataConstants.FlowEngines.StateMachine);

services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
```

The engine key is `SchemataConstants.FlowEngines.StateMachine`, the string `"statemachine"`.
`SchemataFlowBuilder.Use<TProcess>()` defaults to this key when none is given.

`SchemataFlowStateMachineFeature` exposes the engine and validator through
`FlowStateMachineBuilderExtensions.UseStateMachine()` on `SchemataFlowBuilder`.

## Engine key

```csharp
flow.Use<OrderProcess>();                          // "statemachine"
flow.Use<ComplexProcess>(engine: "statemachine");  // explicit, same result
flow.Use<ComplexProcess>(engine: "my-engine");     // custom keyed engine
```

`SchemataConstants.FlowEngines` also defines `Bpmn` (`"bpmn"`), the key under which
`Schemata.Flow.Bpmn.BpmnEngine` registers itself when `UseBpmn()` is on.

## What the engine does

`StateMachineEngine` implements the four `IFlowRuntime` methods over a stateless traversal:

| Method                    | Behavior                                                                                                                                                                                                          |
| ------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `StartAsync`              | Finds the start event, follows its outgoing flow, and writes the first transition row. A none task that awaits a gateway parks at the gateway during this walk.                                                   |
| `TriggerAsync`            | Matches the trigger against the waiting element (event-based gateway, intermediate catch, or boundary) and advances to the matched target. Throws `STATE_MACHINE_INVALID_TRIGGER` when no outgoing flow matches.  |
| `AdvanceAsync`            | Auto-traverses unconditional flows and exclusive gateways; stops at an event-based gateway (keeping the completed activity as `StateName`), an intermediate catch event, or a procedure task body. Returns the input snapshot unchanged when the token is parked. |
| `FindTriggerTargetsAsync` | Returns the single token canonical name when one trigger can consume the supplied event; returns an empty list otherwise. Used by `FlowRunner.ResolveTargetAsync` to look up the token to address.                |

The engine also runs `ProcedureTaskBase.InvokeAsync(FlowTaskContext)` while resolving the next
hop, threading `FlowTaskContext` through every procedure task body so source bindings and
repositories stay in scope.

All state arrives on the `SchemataProcess`, the `SchemataProcessToken` rows, and the
`SchemataProcessSource` rows, and leaves on the returned `ProcessSnapshot`. The snapshot's
`Process` is mutated in place; tokens carry the next `StateName` / `WaitingAtName`; transitions
hold one `SchemataProcessTransition` row (`TransitionKind.Move` for normal advances, `Cancel` for
explicit cancels, `Fail` for advisor failures). The handler persists the snapshot under its unit
of work.

`ResolveSingleToken` enforces the single-token contract at runtime: zero tokens raise
`PROCESS_TOKEN_NOT_FOUND`, more than one raises `PROCESS_TOKEN_AMBIGUOUS`, a mismatched explicit
`tokenName` raises `PROCESS_TOKEN_NOT_FOUND`. These rejections live in the engine, not the
validator; a definition that resolves to multiple tokens fails on the first trigger.

## Validator

`StateMachineFlowEngineValidator` implements `IFlowEngineValidator` with `EngineName` =
`"statemachine"` and delegates to the static `StateMachineValidator.Validate`. It runs at
registration for every process on this engine. See [Validator](validator.md) for the rules.

## Limitations

The engine keeps exactly one live token. The validator rejects (and the engine raises
`STATE_MACHINE_REQUIRES_BPMN_ENGINE` against) the AST shapes a single-token walk cannot execute:

- `ParallelGateway`, `InclusiveGateway`, `ComplexGateway`
- Non-interrupting boundary events (`FlowEvent[Boundary,non-interrupting]`)
- `SubProcess` (any kind) and `CallActivity`
- Any `LoopCharacteristics` (standard or multi-instance)
- `EventBasedGateway` with `Parallel == true`

The engine also refuses to start when the static validator has not been bypassed, so a definition
that uses these shapes fails fast at startup rather than at runtime on a specific path.

## Plugging in a custom engine

Register an alternative as a keyed singleton, then point processes at it:

```csharp
services.TryAddKeyedSingleton<IFlowRuntime, MyParallelEngine>("parallel");

builder.UseFlow().Use<ComplexProcess>(engine: "parallel");
```

The state machine remains registered and keeps serving processes on the default key, because
`TryAddKeyedSingleton` and `TryAddEnumerable` are non-replacing.

## Extension points

- Implement `IFlowRuntime` and register it under a new key to add an engine.
- Implement `IFlowEngineValidator` and register via `TryAddEnumerable` to validate a custom engine.

## Caveats

- `TryAddKeyedSingleton` means a registration of `IFlowRuntime` under `"statemachine"` made before
  `SchemataFlowStateMachineFeature` runs wins, which is how you replace the default engine
  without removing the feature.
- `SchemataFlowStateMachineFeature` is gated on `SchemataFlowFeature`; chain `.UseFlow()` before
  `.UseStateMachine()` so the foundation services exist.
- `ResolveSingleToken` throws before the engine inspects state; a persisted process whose token
  count is wrong must be repaired out of band.

## See also

- [Engine](engine.md)
- [Validator](validator.md)
- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [BPMN Engine](bpmn-engine.md)
