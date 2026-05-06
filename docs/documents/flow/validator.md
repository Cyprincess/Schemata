# State Machine Validator

> **Source:** `Schemata.Flow.StateMachine/StateMachineValidator.cs`

The `StateMachineValidator` ensures a `ProcessDefinition` only uses BPMN elements supported by the single-token state machine engine. It is called automatically during process registration when the engine is `"StateMachine"`.

## Validation Rules

All violations throw `FailedPreconditionException`.

| # | Rule | Reasoning |
|---|------|-----------|
| 1 | Exactly one Start event | A process must have one entry point |
| 2 | Start event has exactly one outgoing `SequenceFlow` | The initial token follows a single path |
| 3 | At least one End event | Every path must eventually terminate |
| 4 | Only `ExclusiveGateway` or `EventBasedGateway` | Parallel/Inclusive gateways require multi-token semantics |
| 5 | `EventBasedGateway` has ≥ 1 outgoing, each targeting `IntermediateCatchEvent` | Event gateways branch on catch events |
| 6 | No non-interrupting Boundary events (`Interrupting == true`) | Non-interrupting events fire concurrently — requires a second token |
| 7 | Boundary event `AttachedTo` resolves to an existing Activity | Dangling references produce runtime errors |
| 8 | Boundary event has exactly one outgoing flow | A boundary event represents a single escape path |
| 9 | No `SubProcess`, `CallActivity`, or `EventSubProcess` | Sub-processes require their own execution context |
| 10 | No `LoopCharacteristics` on Activities | Loops require iteration-over-instance management |
| 11 | Each Activity has at most one outgoing path type | Mixing `Go` and `Decide`/`Fork`/`Await` creates ambiguous routing |

## Usage

```csharp
// Called automatically during registration:
var registry = services.GetRequiredService<IProcessRegistry>();
await registry.RegisterAsync<ApprovalProcess>(engine: "StateMachine");
// StateMachineValidator.Validate(definition) is called internally
```

For programmatic validation:

```csharp
var definition = new ApprovalProcess();
StateMachineValidator.Validate(definition);
```
