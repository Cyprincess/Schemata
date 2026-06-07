# State Machine Engine

`Schemata.Flow.StateMachine` ships the default BPMN execution engine for the Flow subsystem. `SchemataFlowFeature` wires it during `ConfigureServices`, so calling `UseFlow()` is enough to make the state-machine engine the default `IFlowRuntime` for every process registered without an explicit engine name.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.StateMachine` | `StateMachineEngine.cs`, `StateMachineFlowEngineValidator.cs`, `StateMachineValidator.cs` |
| `Schemata.Flow.Foundation` | `Features/SchemataFlowFeature.cs` (registration site) |

## How it gets registered

`SchemataFlowFeature.ConfigureServices` contains these two lines:

```csharp
services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(
    SchemataConstants.FlowEngines.StateMachine);

services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
```

The engine key is `SchemataConstants.FlowEngines.StateMachine` (the string `"StateMachine"`). When `FlowBuilder.Use<TProcess>()` is called without an explicit engine name, it defaults to this key.

## Engine key

```csharp
// SchemataConstants.FlowEngines.StateMachine == "StateMachine"
flow.Use<OrderProcess>();                          // uses StateMachine
flow.Use<OrderProcess>(engine: "StateMachine");    // explicit, same result
flow.Use<OrderProcess>(engine: "my-engine");       // custom engine
```

## What the engine does

`StateMachineEngine` implements `IFlowRuntime` with three methods:

| Method | Behavior |
| --- | --- |
| `StartAsync` | Finds the single start event, follows its outgoing flow, resolves the initial state |
| `TriggerAsync` | Matches the trigger against waiting events (by reference, then by name+type), advances to the matched target |
| `AdvanceAsync` | Auto-traverses unconditional flows and exclusive gateways; stops at `EventBasedGateway` or `IntermediateCatchEvent` |

All state is passed in via `SchemataProcess` and returned as a new `ProcessInstance`. The engine itself is stateless and safe to use as a singleton.

## Validator

`StateMachineFlowEngineValidator` delegates to the static `StateMachineValidator.Validate(definition)`. It runs at registration time (inside `ProcessRegistry.RegisterAsync`) for every process whose engine is `"StateMachine"`. See [Validator](validator.md) for the full rule set.

## Limitations

The state machine engine supports a single execution token. The following BPMN elements are rejected by the validator and will throw `FailedPreconditionException` if encountered at runtime:

- `ParallelGateway`
- `InclusiveGateway`
- Non-interrupting boundary events
- Sub-processes and call activities
- Loop characteristics

## Plugging in a custom engine

To replace or supplement the state machine engine, implement `IFlowRuntime` and register it as a keyed singleton:

```csharp
services.TryAddKeyedSingleton<IFlowRuntime, MyParallelEngine>("parallel");
```

Then register processes against it:

```csharp
builder.UseFlow(flow => {
    flow.Use<ComplexProcess>(engine: "parallel");
});
```

The `StateMachineEngine` remains registered and continues to serve processes that use the default key.

## Extension points

- Implement `IFlowEngineValidator` and register via `TryAddEnumerable` to add validation rules for the state machine engine or for a custom engine.
- The `IFlowRuntime` keyed service pattern means multiple engines can coexist in the same application.

## Design motivation

Bundling the default engine inside `SchemataFlowFeature` keeps the common case (single-token state machine) to a single `UseFlow()` call. Applications that need a different engine register it alongside the default under a different keyed-singleton key.

## Caveats

- `TryAddKeyedSingleton` means a custom registration of `IFlowRuntime` under the `"StateMachine"` key registered before `SchemataFlowFeature` runs takes precedence. This is intentional and allows replacing the default engine without removing the feature.
- The engine has no dedicated `Use*` extension on `SchemataBuilder`. Application startup wires it transitively through `UseFlow()`.

## See also

- [Engine](engine.md)
- [Validator](validator.md)
- [Overview](overview.md)
- [Runtime Services](runtime.md)
