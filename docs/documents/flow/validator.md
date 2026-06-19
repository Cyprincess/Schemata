# Flow Validator

`StateMachineValidator` checks that a `ProcessDefinition` uses only what the single-token engine can
execute. `StateMachineFlowEngineValidator` adapts it to the `IFlowEngineValidator` contract and is
invoked during registration for every process whose engine is `"statemachine"`. All violations throw
`FailedPreconditionException`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.StateMachine` | `StateMachineValidator.cs`, `StateMachineFlowEngineValidator.cs` |
| `Schemata.Flow.Skeleton` | `Runtime/IFlowEngineValidator.cs` |

## IFlowEngineValidator

```csharp
public interface IFlowEngineValidator
{
    string EngineName { get; }
    void   Validate(ProcessDefinition definition);
}
```

`ProcessRegistry.RegisterAsync` calls `Validate` on every registered `IFlowEngineValidator` whose
`EngineName` matches the configuration's engine. `StateMachineFlowEngineValidator.EngineName` returns
`SchemataConstants.FlowEngines.StateMachine`, and its `Validate` delegates to the static
`StateMachineValidator.Validate(definition)`.

## Validation rules

`StateMachineValidator.Validate` runs these checks in order:

| Area | Rule |
| --- | --- |
| Start | Exactly one start event, with exactly one outgoing flow. |
| End | At least one end event; end events have no outgoing flows. |
| Names | Element names are unique (they are the persisted state labels). |
| Flows | Every flow has a source and target that both exist in the definition. |
| Gateways | Only `ExclusiveGateway` and `EventBasedGateway` are allowed. |
| Event gateway | `Parallel == false`; at least one outgoing flow; every outgoing flow targets an intermediate catch event. |
| Exclusive gateway | At least one outgoing flow. |
| Boundary events | Attached to an existing activity; interrupting; exactly one outgoing flow. |
| Intermediate catch | At least one outgoing flow; reachable only from an event-based gateway. |
| Activities | Exactly one outgoing path type (no mixing direct flow, gateway, and end-event edges); at most one direct flow to another activity. |
| Sub-processes / loops | No `SubProcess`, `CallActivity`, or `LoopCharacteristics`. |
| Reachability | Every element is reachable from the start event; boundary events count as reachable through their host activity. |

The messages name the offending element by its `Name` rather than its CLR type, so a modeling error
points at the graph node the author wrote.

## Usage

Validation runs automatically when a process is registered, whether at startup or at runtime:

```csharp
// Startup, via SchemataFlowFeature:
flow.Use<ApprovalProcess>();

// Runtime, via IProcessRegistry:
await registry.RegisterAsync<ApprovalProcess>();
```

The static entry point validates a definition without the DI container, which is convenient in unit
tests:

```csharp
StateMachineValidator.Validate(new ApprovalProcess());   // throws on violation
```

## Extension points

- Implement `IFlowEngineValidator` and register via `TryAddEnumerable`. A custom validator runs only
  for the engine named in its `EngineName`.

## Design rationale

Validating at registration — startup for `flow.Use<T>()` — means an invalid definition fails fast at
startup, before any request reaches the engine. Modeling errors surface in the build/boot loop instead
of at runtime on a specific path.

## Caveats

- The validator checks structure only. It does not verify that guard expressions are sound or that
  the variable keys a `When<T>` reads exist at runtime.
- `StateMachineValidator.Validate` is static; it can be called without DI.

## See also

- [Engine](engine.md)
- [AST Reference](ast.md)
- [State Machine](state-machine.md)
