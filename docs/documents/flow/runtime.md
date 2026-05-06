# Flow Runtime Services

> **Source:** `Schemata.Flow.Foundation/`, `Schemata.Flow.Http/`, `Schemata.Flow.Grpc/`

## ProcessRuntime

`ProcessRuntime` implements `IProcessRuntime` and coordinates persistence with the flow engine. Registered as **scoped** by `UseFlow()`.

### Responsibilities

1. Look up the `ProcessDefinition` from `ProcessRegistry`.
2. Select the correct `IFlowRuntime` engine via `IFlowRuntimeRegistry`.
3. Serialize/deserialize variables via `VariableSerializer`.
4. Persist state changes through `IRepository<SchemataProcess>`.
5. Record transitions as `SchemataProcessTransition` rows.

### Methods

| Method | Description |
|--------|-------------|
| `StartAsync(processName, variables, principal, ct)` | Creates a `SchemataProcess` entity, runs `engine.StartAsync`, persists |
| `StartAsync(definitionName, displayName, description, variables, principal, ct)` | Extended overload — sets `DisplayName` and `Description` on the instance |
| `TriggerAsync(instanceName, trigger, payload, principal, ct)` | Finds the process, runs `engine.TriggerAsync`, persists + records transition |
| `SendMessageAsync(instanceName, messageName, payload, principal, ct)` | Resolves the `Message` definition, then calls `TriggerAsync` |
| `SendSignalAsync(signalName, payload, principal, ct)` | Broadcasts to all processes waiting at matching `IntermediateCatch` events |

## ProcessRegistry

`ProcessRegistry` implements `IProcessRegistry`. Registered as **singleton** by `UseFlow()`.

### Registration Flow

1. `ProcessConfiguration` is constructed (name, engine, `DefinitionType`).
2. The `ProcessDefinition` subclass is instantiated — the constructor runs the DSL.
3. If the engine is `"StateMachine"`, `StateMachineValidator.Validate(definition)` is called.
4. The registration is stored keyed by name.

## FlowRuntimeRegistry

`FlowRuntimeRegistry` implements `IFlowRuntimeRegistry`. Registered as **singleton** by `UseFlow()`.

```csharp
public interface IFlowRuntimeRegistry
{
    void Register(IFlowRuntime runtime);
    IFlowRuntime? GetRuntime(string engineName);
}
```

## SchemataProcess Entity

```csharp
[Table("SchemataProcesses")]
public class SchemataProcess :
    IIdentifier, ICanonicalName, ITimestamp, IStateful, IDescriptive
{
    long ProcessDefinitionId         // hash of the ProcessDefinition
    string ProcessDefinitionName     // registered name
    string? Variables                // JSON-serialized variables
    string? State                    // current element name
    string? WaitingAt                // event/gateway name (null = active)
}
```

## SchemataProcessTransition Entity

Records each state transition with `Previous`, `Posterior`, `Event`, `UpdatedByName`, and timestamps. Table: `SchemataProcessTransitions`.

## HTTP Endpoints

Mounted at `~/processes`. Provided by `ProcessController` in `Schemata.Flow.Http`.

| Method | Path | Request Body | Description |
|--------|------|-------------|-------------|
| `GET` | `~/processes` | — | List all process instances |
| `GET` | `~/processes/{name}` | — | Get instance by canonical name segment |
| `POST` | `~/processes` | `StartProcessRequest` | Start a new instance |
| `PATCH` | `~/processes/{name}` | `UpdateProcessRequest` | Update instance metadata |
| `DELETE` | `~/processes/{name}` | — | Delete an instance |
| `POST` | `~/processes/{name}:trigger` | `TriggerProcessRequest` | Trigger an event on an instance |
| `GET` | `~/processes/{name}/transitions` | — | List transition history |
| `GET` | `~/processes/{name}/transitions/{tName}` | — | Get a single transition record |
| `GET` | `~/processes/:definitions` | — | List registered process definition names |

### Request DTOs

```csharp
public sealed class StartProcessRequest
{
    string DefinitionName           // registered process definition name
    string? DisplayName
    string? Description
    string? Variables               // JSON-serialized initial variables
}

public sealed class TriggerProcessRequest
{
    string EventName                // event definition name to trigger
    string? Payload                 // JSON-serialized payload merged into variables
}

public sealed class UpdateProcessRequest
{
    string? DisplayName
    string? Description
    string? State
    string? Variables
}
```

## UseFlow() Registration

```csharp
public static SchemataBuilder UseFlow(
    this SchemataBuilder builder,
    Action<FlowBuilder>? configure = null)
```

Registers:
- `IFlowRuntimeRegistry` → `FlowRuntimeRegistry` (singleton)
- `IProcessRegistry` → `ProcessRegistry` (singleton)
- `IFlowRuntime` → `StateMachineEngine` (singleton)
- `ProcessRuntime` (scoped)
- `SchemataFlowFeature` (wires middleware and endpoint pipeline)

The `FlowBuilder` callback pre-registers process definitions:

```csharp
schema.UseFlow(flow => {
    flow.Use<ApprovalProcess>(engine: "StateMachine");
});
```
