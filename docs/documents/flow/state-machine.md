# State Machine Engine

`Schemata.Flow.StateMachine` is the default execution engine for Flow. `SchemataFlowFeature` wires
it during `ConfigureServices`, so `UseFlow()` alone makes the state machine the `IFlowRuntime` for
every process registered without an explicit engine name.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.StateMachine` | `StateMachineEngine.cs`, `StateMachineFlowEngineValidator.cs`, `StateMachineValidator.cs` |
| `Schemata.Flow.Foundation` | `Features/SchemataFlowFeature.cs` (registration site) |

## How it gets registered

`SchemataFlowFeature.ConfigureServices` registers the engine and its validator:

```csharp
services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(
    SchemataConstants.FlowEngines.StateMachine);

services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
```

The engine key is `SchemataConstants.FlowEngines.StateMachine`, the string `"statemachine"`.
`SchemataFlowBuilder.Use<TProcess>()` defaults to this key when none is given.

## Engine key

```csharp
flow.Use<OrderProcess>();                          // "statemachine"
flow.Use<OrderProcess>(engine: "statemachine");    // explicit, same result
flow.Use<ComplexProcess>(engine: "my-engine");     // custom keyed engine
```

`SchemataConstants.FlowEngines` also defines `Bpmn` (`"bpmn"`) as a reserved key for a full BPMN
engine; no implementation registers under it.

## What the engine does

`StateMachineEngine` implements the three `IFlowRuntime` methods over a stateless traversal:

| Method | Behavior |
| --- | --- |
| `StartAsync` | Finds the start event, follows its outgoing flow, resolves the initial state. |
| `TriggerAsync` | Matches the trigger against the waiting element (by reference, then name and type) and advances to the matched target. |
| `AdvanceAsync` | Auto-traverses unconditional flows and exclusive gateways; stops at an event-based gateway or an intermediate catch event. |

All state arrives on the `SchemataProcess` argument and leaves on the returned `ProcessInstance`, so
the engine is a safe singleton. See [Engine](engine.md) for the full traversal and matching rules.

## Validator

`StateMachineFlowEngineValidator` implements `IFlowEngineValidator` with `EngineName` =
`"statemachine"` and delegates to the static `StateMachineValidator.Validate`. It runs at
registration for every process on this engine. See [Validator](validator.md) for the rules.

## Limitations

The engine carries a single token. The validator rejects, and the engine would otherwise throw
`FailedPreconditionException` on:

- `ParallelGateway`, `InclusiveGateway`, `ComplexGateway`
- Non-interrupting boundary events
- Sub-processes and call activities
- Loop characteristics (single and multi-instance)

## Plugging in a custom engine

Register an alternative as a keyed singleton, then point processes at it:

```csharp
services.TryAddKeyedSingleton<IFlowRuntime, MyParallelEngine>("parallel");

builder.UseFlow().Use<ComplexProcess>(engine: "parallel");
```

The state machine remains registered and keeps serving processes on the default key.

## Extension points

- Implement `IFlowRuntime` and register it under a new key to add an engine.
- Implement `IFlowEngineValidator` and register via `TryAddEnumerable` to validate a custom engine.

## Design rationale

Bundling the default engine inside `SchemataFlowFeature` keeps the common case — a single-token
state machine — to one `UseFlow()` call. Alternate engines coexist under different keys.

## Caveats

- `TryAddKeyedSingleton` means a registration of `IFlowRuntime` under `"statemachine"` made before
  `SchemataFlowFeature` runs wins, which is how you replace the default engine without removing the
  feature.
- The engine has no dedicated `Use*` method; `UseFlow()` wires it.

## See also

- [Engine](engine.md)
- [Validator](validator.md)
- [Overview](overview.md)
- [Runtime Services](runtime.md)
