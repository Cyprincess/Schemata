# Schemata.Flow.Bpmn — AGENTS

## OVERVIEW

Full BPMN 2.0.2 multi-token engine. `BpmnEngine` implements `IFlowRuntime` and `ICompensationExecutor`; entry points dispatch via `is`-pattern cascades to specialized executors.

## STRUCTURE

| Path | Role |
|---|---|
| `BpmnEngine.cs` | Orchestrator; owns `Dictionary<string, CompensationStack> _compensationScopes` (runtime memory only, not rehydrated on restart) |
| `BpmnValidator.cs` | Static structural validator (no DI) |
| `BpmnFlowEngineValidator.cs` | Adapts `BpmnValidator` to `IFlowEngineValidator` (TryAddEnumerable) |
| `Features/SchemataFlowBpmnFeature.cs` | `[DependsOn<SchemataFlowFeature>]`; registers keyed singletons under `SchemataConstants.FlowEngines.Bpmn` |
| `Extensions/FlowBpmnBuilderExtensions.cs` | `UseBpmn()` on `SchemataFlowBuilder` |
| `Runtime/Boundary/` | `CompensationBoundaryHandler`, `EscalationBoundaryHandler` (uses Skeleton scope-chain indexes for escalation/error routing; BPMN 2.0.2 §10.5.1 / §10.5.6 / §13.5.3), `NonInterruptingBoundaryHandler` (sibling spawn, host stays live) |
| `Runtime/Compensation/` | `ICompensationHandler`, `BoundaryCompensationHandler`, `CompensationThrowHandler` (targeted = reverse snapshot scan; global = `CompensationCoordinator`), `CompensationCoordinator` (reverse registration order, first failure stops), `CompensationStack` (non-thread-safe list), `CompensationInvocationContext`, `CompensationResult` |
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
- Static gateway handlers take no DI dependencies.
- DI registration: keyed singleton `IFlowRuntime` under `SchemataConstants.FlowEngines.Bpmn` (engine key `"bpmn"`).

## ANTI-PATTERNS / GOTCHAS

| Trap | Rule |
|---|---|
| Engine-neutral types here | Belong in `Flow.Skeleton`. This module is BPMN 2.0.2 specifics. |
| Direct `NotImplementedException` | Use the `NotImplemented(feature)` helper, which raises `FailedPreconditionException` with the `BPMN_NOT_IMPLEMENTED` reason key. |
| Relying on compensation scope across restart | `_compensationScopes` is runtime memory only; stacks are not rehydrated after a process restart. |
| Throwing from `ICompensationLifecycleObserver` | Errors are swallowed. Log and continue. |
| MultiInstance with `MIEventBehavior.One` or `.Complex` | Throws `NotSupportedException`. Use `.None` or `.All`. |
| Transparent gateway with `in>1 && out>1` | Surfaces `BPMN_TRANSPARENT_GATEWAY_NOT_SUPPORTED`. |
| State-machine engine hitting BPMN-only features | Surfaces `STATE_MACHINE_REQUIRES_BPMN_ENGINE`; switch to `UseBpmn()`. |
| Out-of-scope constructs | Link/multiple events, ad-hoc sub-process, data objects, collaboration/pool/lane. MIWG exclusions: `tests/Schemata.Flow.Bpmn.Conformance.Tests/PendingCatalog.cs`. |

## READING ORDER

`BpmnEngine.cs` → `EscalationBoundaryHandler.cs` → `Compensation/*` → `Gateways/*` → `MultiInstanceExecutor` + `CallActivityExecutor` → `TransactionExecutor` → `Features` wiring → `BpmnValidator.cs`.

Maintainer doc kept in sync with code: `docs/documents/flow/bpmn-engine.md`. Conformance suite at `tests/Schemata.Flow.Bpmn.Conformance.Tests`; exclusions live in `PendingCatalog.cs`.
