# Schemata.Flow.Foundation

## OVERVIEW

23 files, ~2782 LOC. The runtime host for the Flow domain: process registry, persistence, runner, resource handlers, bridge enforcement. The AST and engine contracts live in `Schemata.Flow.Skeleton` (own AGENTS.md); the full BPMN engine lives in `Schemata.Flow.Bpmn` (own AGENTS.md). This package owns no execution semantics — it owns the scope, the registry and the write path.

## ENTRY POINTS

- `UseFlow(this SchemataBuilder)` in [Extensions/FlowBuilderExtensions.cs](Extensions/FlowBuilderExtensions.cs) returns `SchemataFlowBuilder`.
- `Use<TProcess>(engine?, configure?)` writes a `ProcessConfiguration` into `SchemataFlowOptions.Configurations`; the default engine key is `FlowConstants.Engines.StateMachine` (`"statemachine"`). [Features/SchemataFlowFeature.cs](Features/SchemataFlowFeature.cs) consumes those configurations at `ConfigureServices` time.

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

`FlowExecutionContext` is then built with `scope.UnitOfWork` plus the same provider. Engines are keyed **singletons** and hold no provider of their own — they read everything off the execution context.

**Scope isolation.** The unit the invariant is stated over is one process operation or one signal delivery, never the outer broadcast coordinator. Ordinary entry points run on the runner's own scope. `ThrowSignalAsync` instead snapshots candidate identities in a short-lived discovery scope, drains it, and then creates one `AsyncServiceScope` per candidate and resolves the delivery `FlowRunner` from that scope. Only canonical identifiers cross the coordinator boundary: each delivery reloads its process and tokens inside its own unit of work, and its five repositories, advisors, observers, `AdviceContext` and `FlowExecutionContext.Services` all come from that same delivery scope and are never shared with another delivery. `IProcessRegistry`, `ProcessPersistence` and the keyed `IFlowRuntime` stay shared singletons. **One delivery, one scope, one unit of work.**

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

- **Catches are checked at run time, not registration.** `FlowRunner.EnsureCatchesHaveHandlers` walks snapshot transitions after every engine call; a token parked at a Message/Signal/Timer catch throws `FailedPreconditionException` unless some registered `IFlowCatchHandler` answers `Handles(kind)`. The diagnostic names the catch and its kind and never the package that would supply a handler — the question is whether the catch has an owner, not what is installed. A definition registers fine and fails on the first park.
- **Advice and arming are two contracts, run in that order, and only one of them is optional.** Per transition `FlowRunner` first runs the ordered `IFlowTransitionAdvisor` pipeline — the general extension point, for auditing, projection and invariant enforcement — then calls `IFlowCatchHandler.ArmAsync` on every registered handler. `Advisor.For<IFlowTransitionAdvisor>().RunAsync`'s result is deliberately discarded, against the house convention of switching on it: `Block` / `Handle` end the advisor chain, and arming still runs, because a token parked on a catch nobody armed waits forever. Rejecting a transition means throwing, which aborts before persistence. A handler owns its own arrangement — `FlowEventCatchHandler` (`Schemata.Flow.Event`) reconciles `SchemataEventSubscription` rows, `FlowTimerCatchHandler` (`Schemata.Flow.Scheduling`) schedules and cancels timer jobs and reports a missing `IScheduler` itself.
- **A signal broadcast reports per target and never rolls back across targets.** `ThrowSignalAsync` returns one `SignalDeliveryResult` per candidate process, ordered by canonical name: `Delivered` once its unit of work committed, `NoLongerWaiting` when the process vanished or no longer offers a matching target, `Failed` (carrying the exception) when its delivery threw, `Canceled` when the caller's token cancelled it. A failing target rolls back only itself and the broadcast continues. Cancellation before the candidate snapshot exists propagates; after it, every candidate gets an entry instead. `SchemataFlowOptions.SignalBroadcastConcurrency` (default 8) bounds in-flight deliveries — on SQLite, whose single writer serialises commits anyway, 1 is the honest setting.
- **Delivery notifications are post-commit.** A signal delivery collects its snapshots and notifies `IProcessLifecycleObserver` only after `ProcessPersistence.ExecuteAsync` returned, so a rollback never leaves an observer believing the transition landed. `NotifyFailedAsync` fires for `Failed` only, not for `Canceled`.
- String conditions compile at registration. A definition registered without a matching keyed `IExpressionCompiler` leaves them uncompiled and fails on evaluation.
- `ProcessLifecycleNotifier` swallows `IProcessLifecycleObserver` exceptions (logs only). Never put control flow in an observer.
- Owner queries are suppressed on the joined flow repositories, and `FlowSourceReadScope` additionally suppresses soft-delete filters when reading source entities. Flow sees rows an ordinary request would not.
- `AdviceSourceProjection<TSource>` respects `IConcurrency` and rejects stale writes with reason `FLOW_SOURCE_MODIFIED_CONCURRENTLY`; `FlowSourceWriteBack.Touch<T>` is what enrolls an entity into `execution.TouchedSources`.

Canonical docs: `docs/documents/flow/runtime.md`, `docs/cookbook/flow-with-events.md`, `docs/cookbook/flow-with-timers.md`.
