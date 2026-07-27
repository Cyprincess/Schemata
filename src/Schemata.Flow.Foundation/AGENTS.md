# Schemata.Flow.Foundation

## OVERVIEW

23 files, ~2782 LOC. The runtime host for the Flow domain: process registry, persistence, runner, resource handlers, bridge enforcement. The AST and engine contracts live in `Schemata.Flow.Skeleton` (own AGENTS.md); the full BPMN engine lives in `Schemata.Flow.Bpmn` (own AGENTS.md). This package owns no execution semantics — it owns the scope, the registry and the write path.

## ENTRY POINTS

- `UseFlow(this SchemataBuilder)` in [Extensions/FlowBuilderExtensions.cs](Extensions/FlowBuilderExtensions.cs) returns `SchemataFlowBuilder`.
- `Use<TProcess>(engine?, configure?)` writes a `ProcessConfiguration` into `SchemataFlowOptions.Configurations`; the default engine key is `SchemataConstants.FlowEngines.StateMachine` (`"statemachine"`). [Features/SchemataFlowFeature.cs](Features/SchemataFlowFeature.cs) consumes those configurations at `ConfigureServices` time.

DI lifetimes registered by the feature: `IProcessRegistry` and `ProcessPersistence` are **singleton**; `FlowRunner` / `IFlowRunner` / `ProcessLifecycleNotifier` / `ProcessDefinitionQueryService` and every resource handler are **scoped**; `IFlowSourceAdvisor<>` goes in via `TryAddEnumerable`.

## REGISTRY

[ProcessRegistry.cs](ProcessRegistry.cs) — thread-safe `ConcurrentDictionary` keyed by process name. `Register` does, in order:

1. Instantiate the definition via `ActivatorUtilities.CreateInstance`.
2. Resolve the keyed `IFlowRuntime` for `configuration.Engine`.
3. `ValidateRegistrationCapabilities` walks `definition.AllElements` and demands a capability per shape: `ProcedureTaskBase` → `ProcedureTasks`; parallel / inclusive / complex gateway and `EventBasedGateway.Parallel` → `MultiToken`; `CallActivity` / `SubProcess` → `SubProcesses`; loop characteristics → `Loops`; non-interrupting boundary → `NonInterruptingBoundaries`; `CompensationDefinition` → `Compensation`. Nested sub-processes get a second pass for `NestedEvents` / `NestedTimers`.
4. Run every `IFlowEngineValidator` whose `EngineName` matches.
5. Compile string conditions through `GetKeyedService<IExpressionCompiler>(configuration.Language)`, raising `FLOW_EXPRESSION_LANGUAGE_REQUIRED` or `FLOW_EXPRESSION_LANGUAGE_NOT_REGISTERED`.
6. Build `SourceTypes` and the message/signal payload-type maps.

Duplicate registration throws `AlreadyExistsException`. All of this happens at registration, not at run time.

## THE SINGLE-SCOPE INVARIANT

The defining property of this package. `FlowRunner` is **scoped** and injects `IServiceProvider`. Every public entry point routes through `ExecuteWithNotificationAsync` → `ProcessPersistence.ExecuteAsync(services, action, ct)`, which:

- resolves all five repositories (process, token, transition, source, compensation) from **that same scoped provider**,
- opens **one** unit of work and joins all five into it,
- sets `QueryOwnerSuppressed` on each,
- wraps them in `FlowPersistenceScope`.

`FlowExecutionContext` is then built with `scope.UnitOfWork` plus the same provider. Engines are keyed **singletons** and hold no provider of their own — they read everything off the execution context. One run, one scope, one unit of work.

## PERSISTENCE BOUNDARY

The engine mutates in-memory state only. Foundation writes the returned snapshot via `ProcessPersistence.PersistSnapshotAsync`: upsert process, upsert tokens, insert transitions, replace compensation bindings.

## RESOURCE HANDLERS

Mapping table in [FlowResourceRegistration.cs](FlowResourceRegistration.cs). AIP-136 methods:

| Method | Handler |
|---|---|
| `:start` | `FlowStartProcessHandler` — loads the canonical-named source through `FlowSourceLoader` + `IResourceTypeResolver` |
| `:complete` | `CompleteActivityHandler` |
| `:correlate` | `CorrelateMessageHandler` |
| `:signal` | `ThrowSignalHandler` |
| `:terminate` | `TerminateProcessHandler` |
| token `:cancel` | `CancelTokenHandler` |

Processes, tokens and transitions are exposed read-only (`Get` / `List`).

## GOTCHAS

- **Bridges are enforced at run time, not registration.** `FlowRunner.EnsureBridgeRequirements` walks snapshot transitions after every engine call; a token parked at a Message/Signal/Timer catch throws `FailedPreconditionException` unless `SchemataFlowOptions.Bridges` holds the marker. `"events"` is added by `Schemata.Flow.Event`, `"timers"` by `Schemata.Flow.Scheduling` (`SchemataFlowOptions.EventsBridge` / `TimersBridge`). A definition registers fine and fails on the first park.
- String conditions compile at registration. A definition registered without a matching keyed `IExpressionCompiler` leaves them uncompiled and fails on evaluation.
- `ProcessLifecycleNotifier` swallows `IProcessLifecycleObserver` exceptions (logs only). Never put control flow in an observer.
- Owner queries are suppressed on the joined flow repositories, and `FlowSourceReadScope` additionally suppresses soft-delete filters when reading source entities. Flow sees rows an ordinary request would not.
- `AdviceSourceProjection<TSource>` respects `IConcurrency` and rejects stale writes with reason `FLOW_SOURCE_MODIFIED_CONCURRENTLY`; `FlowSourceWriteBack.Touch<T>` is what enrolls an entity into `execution.TouchedSources`.

Canonical docs: `docs/documents/flow/runtime.md`, `docs/cookbook/flow-with-events.md`, `docs/cookbook/flow-with-timers.md`.
