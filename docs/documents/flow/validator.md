# Flow Validator

The `StateMachineValidator` ensures a `ProcessDefinition` only uses BPMN elements supported by the single-token state machine engine. It is called automatically during process registration when the engine is `"StateMachine"`, via `StateMachineFlowEngineValidator` which implements `IFlowEngineValidator`.

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
    void Validate(ProcessDefinition definition);
}
```

`ProcessRegistry.RegisterAsync` iterates all registered `IFlowEngineValidator` services and calls `Validate` on any whose `EngineName` matches the process configuration's engine. `StateMachineFlowEngineValidator.EngineName` returns `SchemataConstants.FlowEngines.StateMachine`.

## Validation rules

All violations throw `FailedPreconditionException`.

| # | Rule | Reasoning |
| --- | --- | --- |
| 1 | Exactly one start event | A process must have one entry point |
| 2 | Start event has exactly one outgoing `SequenceFlow` | The initial token follows a single path |
| 3 | At least one end event | Every path must eventually terminate |
| 4 | Only `ExclusiveGateway` or `EventBasedGateway` | Parallel/Inclusive gateways require multi-token semantics |
| 5 | `EventBasedGateway` has at least one outgoing flow, each targeting `IntermediateCatchEvent` | Event gateways branch on catch events |
| 6 | No non-interrupting boundary events (`Interrupting == true` required) | Non-interrupting events fire concurrently, requiring a second token |
| 7 | Boundary event `AttachedTo` resolves to an existing `Activity` | Dangling references produce runtime errors |
| 8 | Boundary event has exactly one outgoing flow | A boundary event represents a single escape path |
| 9 | No `SubProcess`, `CallActivity`, or `EventSubProcess` | Sub-processes require their own execution context |
| 10 | No `LoopCharacteristics` on activities | Loops require iteration-over-instance management |
| 11 | Each activity has at most one outgoing path type | Mixing `Go` and `Decide`/`Fork`/`Await` creates ambiguous routing |

## Usage

Validation runs automatically during `IProcessRegistry.RegisterAsync`:

```csharp
// Called automatically at startup via SchemataFlowFeature:
flow.Use<ApprovalProcess>();

// Or at runtime via IProcessRegistry:
await registry.RegisterAsync<ApprovalProcess>(engine: "StateMachine");
```

For programmatic validation outside the DI container:

```csharp
var definition = new ApprovalProcess();
StateMachineValidator.Validate(definition); // throws on violation
```

## Extension points

- Implement `IFlowEngineValidator` and register via `TryAddEnumerable` to add validation rules for a custom engine or to extend the state machine rules.
- The validator is keyed by `EngineName`, so custom validators only run for the engine they declare.

## Design motivation

Running validation at registration time (startup) rather than at execution time means invalid process definitions fail fast and loudly, before any user request reaches the engine. This is preferable to discovering a modeling error only when a specific code path is exercised in production.

## Caveats

- The validator checks structural rules only. It does not verify that condition expressions are syntactically valid or that variable keys referenced in `When<T>` actually exist at runtime.
- `StateMachineValidator.Validate` is a static method and can be called without DI, which is useful in unit tests for process definitions.

## See also

- [Engine](engine.md)
- [AST Reference](ast.md)
- [State Machine](state-machine.md)
