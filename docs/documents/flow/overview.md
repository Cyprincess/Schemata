# Flow

Schemata Flow models business processes as strongly-typed C# classes and runs them against a
persistent store. A `Schemata.Flow.Skeleton.Models.ProcessDefinition` subclass declares a BPMN
graph; an engine advances a token through it in response to external triggers. Every transition
is written as a `SchemataProcessTransition` row, the live process state lives on a
`SchemataProcess` row, and the running tokens live on `SchemataProcessToken` rows. The audit
trail is a byproduct of execution. The default engine `StateMachineEngine` runs a single-token
subset of the BPMN 2.0 AST; richer engines plug in as keyed `IFlowRuntime` services.

## Where the code lives

| Package                      | Key files                                                                                                                                                                                                                                                                                                                      |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Schemata.Flow.Skeleton`     | `Models/` (AST: `ProcessDefinition.cs`, elements, requests), `Builders/` (DSL: `ProcessBuilder.cs`, `ActivityBehavior.cs`), `Runtime/` (`IFlowRuntime.cs`, contexts, registration), `Observers/` (`IFlowTransitionAdvisor.cs`, `IFlowSourceAdvisor.cs`), `Entities/` (process, token, source, transition rows)                 |
| `Schemata.Flow.Foundation`   | `Features/SchemataFlowFeature.cs`, `Builders/SchemataFlowBuilder.cs`, `Extensions/FlowBuilderExtensions.cs`, `IFlowRunner.cs`, `FlowRunner.cs`, `StartProcessOptions.cs`, `ProcessRegistry.cs`, `ProcessPersistence.cs`, `ProcessLifecycleNotifier.cs`, `Advisors/AdviceSourceProjection.cs` |
| `Schemata.Flow.StateMachine` | `StateMachineEngine.cs`, `StateMachineFlowEngineValidator.cs`, `StateMachineValidator.cs`, `Features/SchemataFlowStateMachineFeature.cs`, `Extensions/FlowStateMachineBuilderExtensions.cs`                                                                                                                                    |
| `Schemata.Flow.Bpmn`         | `BpmnEngine.cs`, `BpmnValidator.cs`, `BpmnFlowEngineValidator.cs`, `Features/SchemataFlowBpmnFeature.cs`, `Extensions/FlowBpmnBuilderExtensions.cs`, `Runtime/`                                                                                                                                                                |
| `Schemata.Flow.Event`        | `Features/SchemataFlowEventFeature.cs`, `Internal/AdviceTransitionEvent.cs`, `Internal/FlowEventHandler.cs`                                                                                                                                                                                                                    |
| `Schemata.Flow.Scheduling`   | `Features/SchemataFlowSchedulingFeature.cs`, `Internal/AdviceTransitionTimer.cs`, `Internal/FlowTimerJob.cs`                                                                                                                                                                                                                   |
| `Schemata.Flow.Http`         | `Features/SchemataFlowHttpFeature.cs`, `Controllers/ProcessDefinitionsController.cs`                                                                                                                                                                                                                                           |
| `Schemata.Flow.Grpc`         | `Features/SchemataFlowGrpcFeature.cs`, `Services/ProcessDefinitionService.cs`                                                                                                                                                                                                                                                  |

## Package structure

| Package                      | Role                                                                                |
| ---------------------------- | ----------------------------------------------------------------------------------- |
| `Schemata.Flow.Skeleton`     | AST, entity types, builders, runtime and observer contracts                         |
| `Schemata.Flow.Foundation`   | `UseFlow` feature, registry, runner, persistence, lifecycle notifier                |
| `Schemata.Flow.StateMachine` | Default single-token engine and its validator; activated by `UseStateMachine()`     |
| `Schemata.Flow.Bpmn`         | Full BPMN 2.0.2 multi-token engine and validator; activated by `UseBpmn()`          |
| `Schemata.Flow.Event`        | Bridges message/signal catches to the event bus                                     |
| `Schemata.Flow.Scheduling`   | Bridges timer catches to the scheduler                                              |
| `Schemata.Flow.Http`         | Process execution and read endpoints over HTTP                                      |
| `Schemata.Flow.Grpc`         | Process execution and read endpoints over gRPC                                      |

`Schemata.Flow.StateMachine` ships in its own package and is activated by `UseStateMachine()`.
The feature depends on `SchemataFlowFeature` and registers `StateMachineEngine` as a keyed
singleton `IFlowRuntime` under `SchemataConstants.FlowEngines.StateMachine` (the string
`"statemachine"`), plus `StateMachineFlowEngineValidator` as an enumerable
`IFlowEngineValidator`. `Schemata.Flow.Bpmn` is a separate opt-in package activated by
`UseBpmn()` and selected with `flow.Use<TProcess>(engine: "bpmn")`; see [BPMN Engine](bpmn-engine.md)
and [BPMN Inclusive Merge](bpmn-inclusive-merge.md).

## Startup

`UseFlow()` on `SchemataBuilder` activates `SchemataFlowFeature` and returns a
`SchemataFlowBuilder` that registers process definitions:

```csharp
using Schemata.Flow.Foundation.Extensions;
using Schemata.Flow.StateMachine.Extensions;

builder.UseSchemata(schema => {
    schema.UseFlow()
          .Use<OrderProcess>()                          // default StateMachine engine
          .Use<ComplexProcess>("my-engine");            // custom keyed engine
});
```

`Use<TProcess>()` returns the builder, so definitions chain. An optional
`Action<ProcessConfiguration>` configures the individual definition without breaking the chain.

`SchemataFlowFeature.Priority` is `SchemataConstants.Orders.Extension + 80_000_000` =
`480_000_000`. Its `ConfigureServices` registers:

1. `IProcessRegistry` (`ProcessRegistry`, singleton). The factory eagerly registers every
   configuration through `RegisterAsync`, which validates the definition and resolves its keyed
   engine.
2. `ProcessPersistence` (singleton); `ProcessLifecycleNotifier` (scoped).
3. `FlowRunner` and `IFlowRunner` (scoped).
4. `IFlowSourceAdvisor<>` (scoped, enumerable) for source-bound advisors.
5. The six resource-method handlers: `StartProcessHandler`, `CompleteActivityHandler`,
   `CorrelateMessageHandler`, `ThrowSignalHandler`, `TerminateProcessHandler`,
   `CancelTokenHandler` (scoped).

The engine, its validator, and the `IFlowRuntime` keyed registrations come from
`SchemataFlowStateMachineFeature`, which `[DependsOn<SchemataFlowFeature]`.

## Authorization

Flow endpoints ride the Resource pipeline: enable authorization through the resource builder —
`schema.UseResource().WithAuthorization(scheme)` — and the flow resources and verbs pass through
the resource anonymous + authorize advisors, which call the access and entitlement providers with
the verb as the operation. Per-definition rules belong to custom `IAccessProvider` implementations
reading the request; in-process engine paths (event and scheduling bridges) carry no principal and
no authorization. See [Security](../security.md).

## Feature priority table

| Feature                           | Activation           | Priority    |
| --------------------------------- | -------------------- | ----------- |
| `SchemataFlowFeature`             | `schema.UseFlow()`   | 480,000,000 |
| `SchemataFlowStateMachineFeature` | `.UseStateMachine()` | 480,050,000 |
| `SchemataFlowHttpFeature`         | `.MapHttp()`         | 480,100,000 |
| `SchemataFlowGrpcFeature`         | `.MapGrpc()`         | 480,200,000 |
| `SchemataFlowEventFeature`        | `.UseEvent()`        | 480,300,000 |
| `SchemataFlowSchedulingFeature`   | `.UseScheduling()`   | 480,400,000 |
| `SchemataFlowBpmnFeature`         | `.UseBpmn()`         | 480,500,000 |

The chained methods hang off the `SchemataFlowBuilder` that `UseFlow()` returns. Note the
asymmetry: the transports are `MapHttp` / `MapGrpc`, while the bridges and engines are `Use*`
calls. Each feature declares `[DependsOn<SchemataFlowFeature>]`, so `UseFlow()` is pulled in
when missing.

## Architecture

```
ProcessDefinition (C# class, DSL in constructor)
    |
    | validated by IFlowEngineValidator (StateMachineFlowEngineValidator)
    | held by      IProcessRegistry (ProcessRegistry, singleton)
    v
IFlowRunner / FlowRunner (scoped)
    |  resolves the keyed engine, runs advisor pipeline, persists, dispatches observers
    v
IFlowRuntime (StateMachineEngine, keyed singleton) -- computes the next ProcessSnapshot
    |
    | in unit of work: IFlowTransitionAdvisor pipeline (provisions timers / subscriptions)
    | commit:      ProcessPersistence.PersistSnapshotAsync (single unit of work)
    | post-commit: IProcessLifecycleObserver
    v
persisted process row + token rows + transition row + source-binding rows
```

## Roles

- **`IProcessRegistry`** holds the compiled definitions, their `ProcessConfiguration`, and the
  type maps that correlate message and signal payloads to CLR types. Validation runs once, at
  registration.
- **`IFlowRunner`** is the public surface for callers. `FlowRunner` loads instances, invokes the
  engine, runs the transition advisor pipeline before the commit, persists the process,
  token, transition, and source rows in one unit of work, then notifies lifecycle observers.
  Handlers exposed by `SchemataFlowFeature` cover the full operation set: `StartAsync`,
  `CompleteAsync`, `CorrelateAsync`, `ThrowSignalAsync`, `TerminateAsync`, `CancelTokenAsync`.
- **`IFlowRuntime`** is the engine contract. `StateMachineEngine` is stateless: every call
  receives the current `SchemataProcess` plus token set and returns a `ProcessSnapshot` whose
  mutated entities the handler persists under its own unit of work.
- **`IProcessLifecycleObserver`** reacts to runtime events after persistence:
  `OnStartedAsync` / `OnTransitionedAsync` / `OnTerminatedAsync` / `OnFailedAsync`. It is the only
  lifecycle observer interface; the per-token observer path (fork / join / cancel) was removed, so
  per-token reactions belong in a transition advisor.

## Extension points

- **Custom engine** — Implement `IFlowRuntime`, register it as a keyed singleton under your
  engine name, and pass that name to `flow.Use<TProcess>(engine: "...")`.
- **Transition advisors** — Implement `IFlowTransitionAdvisor` (an `IAdvisor<FlowTransitionContext>`)
  and register via `TryAddEnumerable`. They run inside the transition unit of work, before the
  commit, and provision the infrastructure a new waiting state depends on; a thrown advisor aborts
  the transition.
- **Source-bound advisors** — Implement `IFlowSourceAdvisor<TSource>` (an
  `IAdvisor<FlowTransitionContext, TSource>`) for advisors that read the entity bound to the
  current process or token. The default `AdviceSourceProjection<TSource>` projects the token's
  business position onto the bound entity's declared members and writes it back in the same unit
  of work before the commit; see [Runtime Services](runtime.md) for the projection matrix.
- **Lifecycle observers** — Implement `IProcessLifecycleObserver` and register via
  `TryAddEnumerable` to react to `OnStartedAsync`, `OnTransitionedAsync`,
  `OnTerminatedAsync`, and `OnFailedAsync` after the commit.
- **Transport** — Chain `.MapHttp()` or `.MapGrpc()`.
- **Integrations** — Chain `.UseEvent()` to bridge message/signal catches to the event bus,
  and `.UseScheduling()` to bridge timer catches to the scheduler.
- **Custom condition evaluator** — Implement `IConditionExpression` and pass it to
  `ProcessBuilder.When(IConditionExpression)`.

## Design rationale

The definition (a C# class) is decoupled from the engine (a keyed service), so the execution
model can be swapped without rewriting business logic. Persistence is one unit of work per
transition, covering the process row, every live token, the new transition row, and the
source-binding rows; the audit history never diverges from the live state. The transition
advisor pipeline runs before the commit precisely so that a process never persists into a
waiting state whose wake-up infrastructure (a timer job, an event subscription) failed to be
created.

## Caveats

- `SchemataFlowFeature` materializes `IProcessRegistry` by running `RegisterAsync(...)`
  synchronously during singleton construction. Registration instantiates each
  `ProcessDefinition`, which runs its DSL constructor; keep those constructors cheap.
- `StateMachineEngine` runs a subset of the AST. The validator rejects unsupported elements
  (parallel / inclusive / complex gateways, sub-processes, multi-instance loops, non-interrupting
  boundary events) at registration, so a definition that uses them fails fast at startup.
- `SchemataProcess` implements `ISoftDelete`; the default query filter hides tombstoned
  instances. Read them inside a `using (repository.SuppressQuerySoftDelete())` scope.
- `SchemataProcessToken.Bookkeeping` is an engine-private `Dictionary<string, int>` (loop
  counters, execution metadata) persisted as a JSON column through the provider dictionary
  conversion. Engines mutate it directly; application code must not.
- `SchemataProcess` and `SchemataProcessToken` implement `IAnnotatable`. `Annotations` is
  client-owned per AIP-148; engines never write it.

## See also

- [AST Reference](ast.md)
- [DSL Reference](dsl.md)
- [Engine](engine.md)
- [State Machine](state-machine.md)
- [Validator](validator.md)
- [BPMN Engine](bpmn-engine.md)
- [BPMN Inclusive Merge](bpmn-inclusive-merge.md)
- [Runtime Services](runtime.md)
