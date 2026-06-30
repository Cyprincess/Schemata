# Flow

Schemata Flow models business processes as strongly-typed C# classes and runs them against a
persistent store. A `Schemata.Flow.Skeleton.Models.ProcessDefinition` subclass declares a BPMN
graph; an engine advances a single token through it in response to external triggers. Every
transition is written as a `SchemataProcessTransition` row, and the live instance state lives on
a `SchemataProcess` row, so the audit trail is a byproduct of execution. The default engine
`StateMachineEngine` runs a single-token subset of the BPMN 2.0 AST; richer engines plug in as
keyed `IFlowRuntime` services.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Skeleton` | `Models/ProcessDefinition.cs`, `Models/FlowElement.cs`, `Builders/ProcessBuilder.cs`, `Entities/SchemataProcess.cs`, `Entities/SchemataProcessTransition.cs`, `Runtime/IFlowRuntime.cs`, `Runtime/IProcessRuntime.cs`, `Runtime/IProcessRegistry.cs`, `Runtime/IProcessLifecycleObserver.cs`, `Observers/IFlowTransitionAdvisor.cs`, `Observers/FlowTransitionContext.cs` |
| `Schemata.Flow.Foundation` | `Features/SchemataFlowFeature.cs`, `Builders/SchemataFlowBuilder.cs`, `Extensions/FlowBuilderExtensions.cs`, `ProcessRegistry.cs`, `ProcessRuntime.cs`, `ProcessPersistence.cs`, `ProcessInitializer.cs` |
| `Schemata.Flow.StateMachine` | `StateMachineEngine.cs`, `StateMachineFlowEngineValidator.cs`, `StateMachineValidator.cs` |
| `Schemata.Flow.Event` | `Features/SchemataFlowEventFeature.cs`, `Internal/AdviceTransitionEvent.cs`, `Internal/FlowEventHandler.cs` |
| `Schemata.Flow.Scheduling` | `Features/SchemataFlowSchedulingFeature.cs`, `Internal/AdviceTransitionTimer.cs`, `Internal/FlowTimerJob.cs` |
| `Schemata.Flow.Http` | `Features/SchemataFlowHttpFeature.cs`, `Controllers/ProcessDefinitionsController.cs` |
| `Schemata.Flow.Grpc` | `Features/SchemataFlowGrpcFeature.cs`, `Services/ProcessDefinitionService.cs` |

## Package structure

| Package | Role |
| --- | --- |
| `Schemata.Flow.Skeleton` | AST, entity types, builders, runtime and observer contracts |
| `Schemata.Flow.Foundation` | `UseFlow` feature, registry, runtime coordinator, persistence |
| `Schemata.Flow.StateMachine` | Default single-token engine and its validator |
| `Schemata.Flow.Event` | `UseEvent` (on the flow builder) — message/signal catches bridge to the event bus |
| `Schemata.Flow.Scheduling` | `UseScheduling` (on the flow builder) — timer catches bridge to the scheduler |
| `Schemata.Flow.Http` | `MapHttp` — process execution and read endpoints over HTTP |
| `Schemata.Flow.Grpc` | `MapGrpc` — process execution and read endpoints over gRPC |

`StateMachineEngine` ships in its own package but is wired by `SchemataFlowFeature`; there is no
separate `Use*` call to activate it. `Schemata.Flow.Foundation` depends on the Event feature:
`SchemataFlowFeature` carries `[DependsOn<SchemataEventFeature>]` and publishes process lifecycle
events on the bus.

## Startup

`UseFlow` on `SchemataBuilder` activates `SchemataFlowFeature` and returns a `SchemataFlowBuilder`
that registers process definitions:

```csharp
builder.UseSchemata(schema => {
    schema.UseFlow()
          .Use<OrderProcess>()                          // default StateMachine engine
          .Use<ComplexProcess>("my-engine")             // custom keyed engine
          .Use<AuditedProcess>(c => c.WithAuthorization());
});
```

`Use<TProcess>()` returns the builder, so definitions chain. An optional
`Action<ProcessConfiguration>` configures the individual definition without breaking the chain.

`SchemataFlowFeature.Priority` is `SchemataConstants.Orders.Extension + 80_000_000` = `480_000_000`.
Its `ConfigureServices`:

1. Registers four flow lifecycle events in `EventTypeRegistryConfiguration`:
   `ProcessStartedEvent` (`flow.process.started`), `ProcessCompletedEvent`
   (`flow.process.completed`), `ProcessFailedEvent` (`flow.process.failed`), `TransitionMadeEvent`
   (`flow.transition.made`).
2. Registers `IProcessRegistry` (`ProcessRegistry`) as a singleton. The factory eagerly registers
   every configuration, which validates the definition and resolves its keyed engine.
3. Registers `IProcessRuntime` (`ProcessRuntime`) as a singleton.
4. Registers `StateMachineEngine` as a keyed singleton `IFlowRuntime` under
   `SchemataConstants.FlowEngines.StateMachine` (the string `"statemachine"`).
5. Registers `StateMachineFlowEngineValidator` as an enumerable `IFlowEngineValidator` singleton.
6. Adds `ProcessInitializer` as a hosted service. On startup it loads every persisted instance with
   `WaitingAtId != null` back into the runtime cache so waiting instances survive a restart.

## Feature priority table

| Feature | Activation | Priority |
| --- | --- | --- |
| `SchemataFlowFeature` | `schema.UseFlow()` | 480,000,000 |
| `SchemataFlowHttpFeature` | `.MapHttp()` | 480,100,000 |
| `SchemataFlowGrpcFeature` | `.MapGrpc()` | 480,200,000 |
| `SchemataFlowEventFeature` | `.UseEvent()` | 480,300,000 |
| `SchemataFlowSchedulingFeature` | `.UseScheduling()` | 480,400,000 |

The four chained methods hang off the `SchemataFlowBuilder` that `UseFlow` returns. Note the
asymmetry: the transports are `MapHttp`/`MapGrpc`, while the bridges are `UseEvent` and
`UseScheduling`. Each feature declares `[DependsOn<SchemataFlowFeature>]`, so `UseFlow` is pulled in
when missing.

## Architecture

```
ProcessDefinition (C# class, DSL in constructor)
    |
    | validated by IFlowEngineValidator (StateMachineFlowEngineValidator)
    | held by      IProcessRegistry (ProcessRegistry, singleton)
    v
IProcessRuntime (ProcessRuntime, singleton)
    |  drives the keyed engine, persists, dispatches advisors and observers
    v
IFlowRuntime (StateMachineEngine, keyed singleton) -- computes the next ProcessInstance
    |
    | in unit of work: IFlowTransitionAdvisor pipeline (provisions timers / subscriptions)
    | commit:     IRepository<SchemataProcess> + IRepository<SchemataProcessTransition>
    | post-commit: IProcessLifecycleObserver + flow lifecycle events on IEventBus
    v
persisted instance + transition row
```

## Roles

- **`IProcessRegistry`** holds the compiled definitions and resolves the engine for each. Validation
  runs once, at registration.
- **`IProcessRuntime`** is the public surface for callers. It loads instances, invokes the engine,
  runs the transition advisor pipeline before the commit, writes the `SchemataProcess` and
  `SchemataProcessTransition` rows in one unit of work, then notifies lifecycle observers and
  publishes events.
- **`IFlowRuntime`** is the engine contract. The default `StateMachineEngine` is stateless: all
  state arrives on the `SchemataProcess` argument and leaves on the returned `ProcessInstance`.

## Extension points

- **Custom engine** — implement `IFlowRuntime`, register it as a keyed singleton under your engine
  name, and pass that name to `flow.Use<TProcess>(engine: "...")`.
- **Transition advisors** — implement `IFlowTransitionAdvisor` (an `IAdvisor<FlowTransitionContext>`)
  and register via `TryAddEnumerable`. They run inside the transition's unit of work and provision
  the infrastructure a new waiting state depends on; a thrown advisor aborts the transition, rolling
  back any repository writes joined to the unit of work.
- **Lifecycle observers** — implement `IProcessLifecycleObserver` and register via `TryAddEnumerable`
  to react to `OnStartedAsync`, `OnTransitionedAsync`, and `OnTerminatedAsync` after the commit.
- **Transport** — add `MapHttp()` or `MapGrpc()`.
- **Integrations** — chain `.UseEvent()` to bridge message/signal catches to the event bus, and
  `.UseScheduling()` to bridge timer catches to the scheduler, off the `UseFlow` builder.

## Design rationale

The definition (a C# class) is decoupled from the engine (a keyed service), so the execution model
can be swapped without rewriting business logic. Persistence is one unit of work per transition,
covering both the instance row and its transition row, so the audit history never diverges from the
live state. The transition advisor pipeline runs *before* the commit precisely so that a process
never persists into a waiting state whose wake-up infrastructure (a timer job, an event
subscription) failed to be created.

## Caveats

- `SchemataFlowFeature` materializes `IProcessRegistry` by running `RegisterAsync(...)` synchronously
  during singleton construction. Registration instantiates each `ProcessDefinition`, which runs its
  DSL constructor; keep those constructors cheap.
- `StateMachineEngine` runs a subset of the AST. The validator rejects unsupported elements
  (parallel/inclusive/complex gateways, sub-processes, multi-instance loops, non-interrupting
  boundary events) at registration, so a definition that uses them fails fast at startup.
- `SchemataProcess` implements `ISoftDelete`; the default query filter hides tombstoned instances.
  Read them inside a `using (repository.SuppressQuerySoftDelete())` scope.

## See also

- [AST Reference](ast.md)
- [DSL Reference](dsl.md)
- [Engine](engine.md)
- [Runtime Services](runtime.md)
