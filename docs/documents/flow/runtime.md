# Flow Runtime Services

`ProcessRuntime` and `ProcessRegistry` are the two services that bridge the BPMN engine to the rest of the framework. `ProcessRegistry` holds the compiled process definitions and resolves the correct engine for each. `ProcessRuntime` keeps live instances in an in-process cache, drives engine calls, and notifies observers. Persistence is delegated entirely to `IProcessLifecycleObserver` implementations.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Foundation` | `ProcessRuntime.cs`, `ProcessRegistry.cs` |
| `Schemata.Flow.Skeleton` | `Runtime/IProcessRuntime.cs`, `Runtime/IProcessRegistry.cs`, `Entities/SchemataProcess.cs`, `Entities/SchemataProcessTransition.cs` |

## ProcessRegistry

`ProcessRegistry` implements `IProcessRegistry` and is registered as a **singleton** by `SchemataFlowFeature`.

### Registration flow

1. `ProcessConfiguration` is constructed (name, engine, `DefinitionType`).
2. The `ProcessDefinition` subclass is instantiated via `ActivatorUtilities.CreateInstance` — the constructor runs the DSL.
3. All registered `IFlowEngineValidator` services whose `EngineName` matches are called.
4. The keyed `IFlowRuntime` service is resolved to confirm the engine exists.
5. The registration is stored in a `ConcurrentDictionary<string, ProcessRegistration>` keyed by name (case-insensitive).

### Key methods

```csharp
ValueTask RegisterAsync<TProcess>(string? engine = null, Action<ProcessConfiguration>? configure = null, CancellationToken ct = default)
ValueTask RegisterAsync(ProcessConfiguration configuration, CancellationToken ct = default)
ValueTask UnregisterAsync(string processName, CancellationToken ct = default)
IReadOnlyCollection<string> GetRegisteredProcesses()
bool IsRegistered(string name)
ProcessRegistration? GetRegistration(string name)
```

Registering the same name twice throws `AlreadyExistsException`.

## ProcessRuntime

`ProcessRuntime` implements `IProcessRuntime` and is registered as **scoped** by `SchemataFlowFeature`.

### IProcessRuntime methods

| Method | Description |
| --- | --- |
| `StartProcessInstanceAsync(processName, variables, principal, ct)` | Creates a `SchemataProcess`, calls `engine.StartAsync`, persists |
| `CompleteActivityAsync(instanceName, variables, principal, ct)` | Merges variables, calls `engine.AdvanceAsync`, persists |
| `CorrelateMessageAsync(instanceName, messageName, payload, principal, ct)` | Resolves the `Message` definition, calls `engine.TriggerAsync`, persists |
| `ThrowSignalAsync(signalName, payload, principal, ct)` | Broadcasts to all instances with `WaitingAtId != null` that match the signal |
| `TriggerEventAsync(instanceName, trigger, payload, principal, ct)` | Calls `engine.TriggerAsync` with an explicit `IEventDefinition`, persists |
| `TerminateProcessInstanceAsync(instanceName, principal, ct)` | Sets state to `"terminated"`, marks `IsComplete = true`, persists |

### Per-call sequence

Each runtime method (`Complete`, `Correlate`, `Throw`, `Trigger`, `Terminate`) shares the same `ApplyAsync` core:

1. Snapshot `previousState`, `previousWaitingAtId`, `previousWaitingAt` from the cached `SchemataProcess`.
2. Invoke the `IFlowRuntime` driver to compute the new `ProcessInstance`.
3. Copy the driver's result onto the cached `SchemataProcess`.
4. Build a `FlowTransitionContext` and dispatch it to every `IFlowTransitionObserver`. Each observer call is wrapped in its own try/catch; a thrown observer is logged at warning and the next observer still runs.
5. Build a `SchemataProcessTransition` record (no DB write happens in `ApplyAsync` itself):
   - `Process`: the instance's canonical name
   - `Previous`: the state before the engine call
   - `Posterior`: the state after
   - `Event`: the event name that triggered the transition
   - `UpdatedBy`: subject claim as `users/{sub}`, or `Identity.Name`
6. Hand both the `ProcessInstance` and the transition record back to the caller, which dispatches `IProcessLifecycleObserver.OnTransitionedAsync`. Terminal results additionally evict the cached instance and dispatch `OnTerminatedAsync`.

`StartProcessInstance` follows the same `ApplyAsync` core, then dispatches `OnStartedAsync` before `OnTransitionedAsync`.

## SchemataProcess entity

```csharp
[Table("SchemataProcesses")]
[CanonicalName("processes/{process}")]
public class SchemataProcess : IIdentifier, ICanonicalName, IConcurrency, IDescriptive, ISoftDelete, ITimestamp, IStateful
{
    public string  DefinitionName { get; set; }  // registered process definition name
    public string? Variables      { get; set; }  // JSON-serialized variables
    public string? StateId        { get; set; }  // current element ID
    public string? State          { get; set; }  // current element name
    public string? WaitingAtId    { get; set; }  // element ID the instance is waiting at
    public string? WaitingAt      { get; set; }  // element name the instance is waiting at
}
```

`SchemataProcess` implements `ISoftDelete`, so soft-deleted instances are filtered out by default. Use `repository.Once().SuppressQuerySoftDelete()` to query tombstoned instances.

## SchemataProcessTransition entity

```csharp
[Table("SchemataProcessTransitions")]
[CanonicalName("processes/{process}/transitions/{transition}")]
public class SchemataProcessTransition : IIdentifier, ICanonicalName, ITimestamp
{
    public string? Process   { get; set; }  // canonical name of the process instance
    public string? Previous  { get; set; }  // state before transition
    public string? Posterior { get; set; }  // state after transition
    public string? Event     { get; set; }  // event name that triggered the transition
    public string? UpdatedBy { get; set; }  // principal canonical name
}
```

## IFlowTransitionObserver

```csharp
public interface IFlowTransitionObserver
{
    Task OnTransitionedAsync(FlowTransitionContext context, CancellationToken ct = default);
}

public class FlowTransitionContext
{
    public SchemataProcess    Process             { get; set; }
    public ProcessDefinition? Definition          { get; set; }
    public ProcessInstance    Instance            { get; set; }
    public string?            PreviousState       { get; set; }
    public string?            PreviousWaitingAtId { get; set; }
    public string?            PreviousWaitingAt   { get; set; }
    public IEventDefinition?  Trigger             { get; set; }
}
```

`PreviousState` / `PreviousWaitingAtId` / `PreviousWaitingAt` are snapshotted before the engine result is copied onto `Process`. By the time observers see the context, `Process.WaitingAtId` already equals `Instance.WaitingAtId`; the snapshot fields are the only source for the pre-transition values.

`Schemata.Flow.Event` registers `FlowEventTransitionObserver` to manage `Message` / `Signal` subscriptions in `IEventSubscriptionStore`. `Schemata.Flow.Scheduling` registers `FlowTimerTransitionObserver` to schedule and cancel timer jobs through `IScheduler`. Both register through `TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionObserver, ...>())`, so additional observers stack alongside.

## IProcessLifecycleObserver

```csharp
public interface IProcessLifecycleObserver
{
    Task OnStartedAsync(SchemataProcess process, CancellationToken ct = default);
    Task OnTransitionedAsync(SchemataProcess process, SchemataProcessTransition transition, CancellationToken ct = default);
    Task OnTerminatedAsync(SchemataProcess process, CancellationToken ct = default);
}
```

The built-in `SchemataProcessAuditObserver` owns persistence: it writes the `SchemataProcess` row on `OnStartedAsync`, an audit `SchemataProcessTransition` row plus updated process state on `OnTransitionedAsync`, and the terminal state on `OnTerminatedAsync`. Replace or augment it by stacking additional `IProcessLifecycleObserver` implementations through `TryAddEnumerable`.

## Extension points

- Implement `IFlowTransitionObserver` and register via `TryAddEnumerable` to react to per-transition state (notifications, external syncs).
- Implement `IProcessLifecycleObserver` and register via `TryAddEnumerable` to own persistence or react to start / transition / terminate.
- Call `IProcessRegistry.RegisterAsync` at runtime to add process definitions after startup.

## Design motivation

`ProcessRuntime` takes `ClaimsPrincipal?`. Keeping the foundation layer free of `HttpContext` lets the HTTP and gRPC transports project their own request surfaces into a principal before calling in.

## Caveats

- `ProcessRegistry` is a singleton and calls `RegisterAsync(...).AsTask().GetAwaiter().GetResult()` during construction. Avoid registering processes with expensive constructors.
- `ThrowSignalAsync` loads all instances with `WaitingAtId != null` into memory to check signal compatibility. For large deployments, consider indexing `WaitingAtId`.

## See also

- [Engine](engine.md)
- [State Machine](state-machine.md)
- [HTTP Transport](http.md)
- [gRPC Transport](grpc.md)
- [Event Integration](event.md)
- [Scheduling Integration](scheduling.md)
