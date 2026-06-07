# Flow

Schemata Flow is a BPMN 2.0.2 process engine that lets you model business processes as strongly-typed C# classes and execute them against a persistent state store. A process definition declares the graph of activities, gateways, and events; the engine advances a token through that graph in response to external triggers. Every state transition is persisted as a `SchemataProcessTransition` row, giving you a full audit trail without extra instrumentation.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Skeleton` | `Models/ProcessDefinition.cs`, `Models/FlowElement.cs`, `Entities/SchemataProcess.cs`, `Entities/SchemataProcessTransition.cs`, `Runtime/IProcessRegistry.cs`, `Runtime/IProcessRuntime.cs`, `Runtime/IProcessLifecycleObserver.cs`, `Observers/IFlowTransitionObserver.cs`, `Observers/FlowTransitionContext.cs` |
| `Schemata.Flow.Foundation` | `Features/SchemataFlowFeature.cs`, `Builders/FlowBuilder.cs`, `Extensions/FlowBuilderExtensions.cs`, `ProcessRegistry.cs`, `ProcessRuntime.cs`, `ProcessInitializer.cs`, `Observers/SchemataProcessAuditObserver.cs` |
| `Schemata.Flow.StateMachine` | `StateMachineEngine.cs`, `StateMachineFlowEngineValidator.cs`, `StateMachineValidator.cs` |
| `Schemata.Flow.Event` | `Features/SchemataFlowEventFeature.cs`, `Internal/FlowEventTransitionObserver.cs` |
| `Schemata.Flow.Scheduling` | `Features/SchemataFlowSchedulingFeature.cs`, `Internal/FlowTimerTransitionObserver.cs` |
| `Schemata.Flow.Http` | `Features/SchemataFlowHttpFeature.cs`, `Controllers/ProcessController.cs` |
| `Schemata.Flow.Grpc` | `Features/SchemataFlowGrpcFeature.cs`, `Services/ProcessService.cs` |

## Package structure

```
Schemata.Flow.Skeleton        contracts, AST types, entity definitions, observer interfaces
Schemata.Flow.Foundation      DI wiring, ProcessRegistry, ProcessRuntime, ProcessInitializer, audit observer
Schemata.Flow.StateMachine    default engine (registered by Foundation, not a separate feature)
Schemata.Flow.Event           event-based gateway integration (UseFlowEvent)
Schemata.Flow.Scheduling      timer event integration (UseFlowScheduling)
Schemata.Flow.Http            HTTP transport (UseFlowHttp)
Schemata.Flow.Grpc            gRPC transport (UseFlowGrpc)
```

## Startup

`UseFlow` on `SchemataBuilder` activates `SchemataFlowFeature` (Priority `Orders.Extension + 80_000_000` = 480,000,000). The optional delegate pre-registers process definitions at startup:

```csharp
builder.UseSchemata(schema => {
    schema.UseFlow(flow => {
        flow.Use<OrderProcess>();
        flow.Use<OrderProcess, Order>(); // with entity type
    });
});
```

`SchemataFlowFeature.ConfigureServices` does the following:

1. Pops the `Action<FlowBuilder>` from `Configurators`, runs it, and stores the resulting `ProcessConfiguration` list in `SchemataFlowOptions`.
2. Registers `IProcessRegistry` as a singleton. On first resolution the registry loads every configuration, validates it against the matching engine validator, and resolves the keyed `IFlowRuntime` service.
3. Registers `IProcessRuntime` as scoped (`ProcessRuntime`).
4. Registers `StateMachineEngine` as a keyed singleton under `SchemataConstants.FlowEngines.StateMachine`.
5. Registers `StateMachineFlowEngineValidator` as `IFlowEngineValidator`.
6. Registers `SchemataProcessAuditObserver` as `IProcessLifecycleObserver` (scoped). The observer persists the `SchemataProcess` row on start, the `SchemataProcessTransition` row plus updated process state on each transition, and the terminal state on terminate.
7. Registers `ProcessInitializer` so process definitions configured through `flow.Use<T>()` are materialized into the registry at startup.

## Feature priority table

| Feature | Priority |
| --- | --- |
| `SchemataFlowFeature` | 480,000,000 |
| `SchemataFlowHttpFeature` | 480,100,000 |
| `SchemataFlowGrpcFeature` | 480,200,000 |
| `SchemataFlowEventFeature` | 480,300,000 |
| `SchemataFlowSchedulingFeature` | 480,400,000 |

## Architecture

```
ProcessDefinition (C# class, DSL in constructor)
    |
    v validated by
IFlowEngineValidator (StateMachineFlowEngineValidator)
    |
    v registered in
IProcessRegistry (ProcessRegistry, singleton)
    |
    v executed by
IFlowRuntime (StateMachineEngine, keyed singleton)
    |
    v coordinated by
IProcessRuntime (ProcessRuntime, scoped)
    |
    v persisted to
IRepository<SchemataProcess> + IRepository<SchemataProcessTransition>
    |
    v observed by
IFlowTransitionObserver (FlowEventTransitionObserver, FlowTimerTransitionObserver, custom)
IProcessLifecycleObserver (SchemataProcessAuditObserver, custom)
```

## Extension points

- **Custom engine**: implement `IFlowRuntime` and register it as a keyed singleton under your engine name. Pass the name to `flow.Use<TProcess>(engine: "my-engine")`.
- **Transition observers**: implement `IFlowTransitionObserver` and register via `TryAddEnumerable`. Observers run on every state transition with a `FlowTransitionContext` that exposes pre- and post-transition state.
- **Lifecycle observers**: implement `IProcessLifecycleObserver` and register via `TryAddEnumerable` to hook `OnStartedAsync`, `OnTransitionedAsync`, or `OnTerminatedAsync`. Replacing `SchemataProcessAuditObserver` is the path to a custom persistence backend.
- **Transport**: add `UseFlowHttp()` or `UseFlowGrpc()` for HTTP/gRPC endpoints.
- **Event integration**: add `UseFlowEvent()` to wire event-based gateways to the event bus.
- **Timer integration**: add `UseFlowScheduling()` to wire intermediate timer events to the scheduler.

## Design motivation

The process definition (a C# class) is separated from the runtime (a keyed service) so engines can be swapped without touching business logic. The default state machine engine covers the majority of BPMN patterns; custom engines plug in without changing `ProcessRuntime` or the observer pipeline.

## Caveats

- `SchemataFlowFeature` calls `RegisterAsync(...).AsTask().GetAwaiter().GetResult()` during singleton construction. This is a one-time startup cost; avoid registering processes with expensive constructors.
- The `StateMachineEngine` rejects `ParallelGateway` and `InclusiveGateway` at runtime with `FailedPreconditionException`. Use the full BPMN AST against a custom `IFlowRuntime` if these gateways are required.
- Process instances implement `ISoftDelete`. The default repository query filter hides tombstoned rows; use `repository.Once().SuppressQuerySoftDelete()` to read them.

## See also

- [AST Reference](ast.md)
- [DSL Reference](dsl.md)
- [Engine](engine.md)
- [Validator](validator.md)
- [Runtime Services](runtime.md)
- [State Machine](state-machine.md)
- [Event Integration](event.md)
- [Scheduling Integration](scheduling.md)
- [HTTP Transport](http.md)
- [gRPC Transport](grpc.md)

