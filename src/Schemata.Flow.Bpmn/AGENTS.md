# Schemata.Flow.Bpmn — AGENTS

## OVERVIEW

Full BPMN 2.0.2 multi-token engine. `BpmnEngine` implements `IFlowRuntime` and `ICompensationExecutor`; entry points dispatch via `is`-pattern cascades to specialized executors.

## STRUCTURE

| Path | Role |
|---|---|
| `BpmnEngine.cs` | Stateless orchestrator (singleton, no per-process fields). Executes `ProcedureTaskBase` with StateMachine parity; declares `Capabilities = FlowRuntimeCapabilities.All`. Compensation bindings travel on `ProcessSnapshot.CompensationBindings` and are restored from `FlowExecutionContext` on load |
| `BpmnValidator.cs` | Static structural validator (no DI); validates procedure-task payloads and rejects `AdHocSubProcess` / `LinkDefinition` / `MultipleDefinition` outright |
| `BpmnFlowEngineValidator.cs` | Adapts `BpmnValidator` to `IFlowEngineValidator` (TryAddEnumerable) |
| `Features/SchemataFlowBpmnFeature.cs` | `[DependsOn<SchemataFlowFeature>]`; registers keyed singletons under `SchemataConstants.FlowEngines.Bpmn` |
| `Extensions/FlowBpmnBuilderExtensions.cs` | `UseBpmn()` on `SchemataFlowBuilder` |
| `Runtime/Boundary/` | `CompensationBoundaryHandler`, `EscalationBoundaryHandler` (uses Skeleton scope-chain indexes for escalation/error routing; BPMN 2.0.2 §10.5.1 / §10.5.6 / §13.5.3), `NonInterruptingBoundaryHandler` (sibling spawn, host stays live) |
| `Runtime/Compensation/` | `ICompensationHandler`, `BoundaryCompensationHandler`, `CompensationThrowHandler` (targeted = reverse snapshot scan; global = `CompensationCoordinator`), `CompensationCoordinator` (reverse registration order, first failure stops), `CompensationStack` (scope-local LIFO rebuilt from persisted bindings on each throw), `CompensationInvocationContext`, `CompensationResult` |
| `Runtime/Gateways/` | Static handlers (no DI): `ParallelGatewayHandler` (join parks while `waiting+1 < incoming`), `InclusiveGatewayHandler` (dead-path pruning; parks while `HasLiveUpstreamReachableTo`), `EventBasedGatewayHandler` (exclusive vs Parallel fork), `ComplexGatewayHandler` (`ActivationCount` gate, else inclusive) |
| `Runtime/Loops/` | `StandardLoopExecutor` (test-before/after, `loopCounter` in token bookkeeping), `MultiInstanceExecutor` (sequential + parallel MI; aggregate bookkeeping `nrOfInstances`/`nrOfActiveInstances`/`nrOfCompletedInstances`; `MIEventBehavior.One` and `.Complex` throw `NotSupportedException`) |
| `Runtime/SubProcesses/` | `EventSubProcessExecutor` (uses Skeleton scope-chain indexes; interrupting cancels parent-scope tokens), `TransactionExecutor` (cancel-end → `CompensationCoordinator` → `CancelDefinition` boundary or resume parent), `CallActivityExecutor` (resolves via `IProcessRegistry` and uses the shared flow UoW) |
| `Runtime/ICompensationLifecycleObserver.cs` | Observer errors are swallowed |

## KEY SEAMS

**Engine dispatch.** `StartAsync` / `TriggerAsync` / `AdvanceAsync` are `is`-pattern cascades that hand off to specialized executors. Executors reach engine internals via `internal static` builders (`NewTransition`, `NewChildToken`, `ApplyAggregateState`, `TokenView`, `Snapshot`, `MergeBookkeeping`) and `internal` instance helpers on the supplied `BpmnEngine`.

**Scoped execution context.** `BpmnEngine` is a singleton and does not hold an `IServiceProvider`. Flow operations thread the caller's `FlowExecutionContext` into condition evaluation, observers, compensation routing, loops, subprocesses, and `CallActivityExecutor`.

**Persistence boundary.** The engine itself never loads or commits state. `CallActivityExecutor` joins the shared flow UoW because child-process spawn crosses resource boundaries; Foundation resource handlers persist returned snapshots.

**Compensation routing.** Compensation failures route through `EscalationBoundaryHandler.TryFireErrorBoundaryAsync`. No matching boundary → process Failed and throw.

**Scope routing.** Nested sub-process scope maps live in `Schemata.Flow.Skeleton.Utilities.ProcessScopeMap`; BPMN handlers keep only BPMN-specific routing decisions here.

## CONVENTIONS

- BPMN-only types live here, not in `Flow.Skeleton`: compensation runtime types, the engine, and validator. Keeps the AST engine-neutral.
- `ProcedureTaskBase` executes here with StateMachine parity: the engine builds a `FlowTaskContext` per token, awaits `InvokeAsync`, then resolves the outgoing auto-flow; an unresolvable auto-flow parks the token at the procedure name.
- The engine declares `FlowRuntimeCapabilities.All`; `ProcessRegistry` rejects definitions whose shapes exceed the selected engine's declared capabilities at registration, and both validators reject inert AST (`AdHocSubProcess`, `LinkDefinition`, `MultipleDefinition`) — no silent degradation.
- Static gateway handlers take no DI dependencies.
- DI registration: keyed singleton `IFlowRuntime` under `SchemataConstants.FlowEngines.Bpmn` (engine key `"bpmn"`).

## ANTI-PATTERNS / GOTCHAS

| Trap | Rule |
|---|---|
| Engine-neutral types here | Belong in `Flow.Skeleton`. This module is BPMN 2.0.2 specifics. |
| Direct `NotImplementedException` | Use the `NotImplemented(feature)` helper, which raises `FailedPreconditionException` with the `BPMN_NOT_IMPLEMENTED` reason key. |
| Assuming compensation state dies with the engine | Bindings persist per process in `SchemataProcessCompensations` and round-trip through the snapshot; a throw after a restart still runs the registered compensations. A missing binding raises `InvalidOperationException` — never a silent no-op. |
| Throwing from `ICompensationLifecycleObserver` | Errors are swallowed. Log and continue. |
| MultiInstance with `MIEventBehavior.One` or `.Complex` | Throws `NotSupportedException`. Use `.None` or `.All`. |
| Transparent gateway with `in>1 && out>1` | Surfaces `BPMN_TRANSPARENT_GATEWAY_NOT_SUPPORTED`. |
| State-machine engine hitting BPMN-only features | Surfaces `STATE_MACHINE_REQUIRES_BPMN_ENGINE`; switch to `UseBpmn()`. |
| Out-of-scope constructs | Link/multiple events and ad-hoc sub-processes are validator-rejected at registration; data objects and collaboration/pool/lane are unsupported. MIWG exclusions: `tests/Schemata.Flow.Bpmn.Conformance.Tests/PendingCatalog.cs`. |

## READING ORDER

`BpmnEngine.cs` → `EscalationBoundaryHandler.cs` → `Compensation/*` → `Gateways/*` → `MultiInstanceExecutor` + `CallActivityExecutor` → `TransactionExecutor` → `Features` wiring → `BpmnValidator.cs`.

Maintainer doc kept in sync with code: `docs/documents/flow/bpmn-engine.md`. Conformance suite at `tests/Schemata.Flow.Bpmn.Conformance.Tests`; exclusions live in `PendingCatalog.cs`.
