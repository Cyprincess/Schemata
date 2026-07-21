# Schemata.Flow.Skeleton

## OVERVIEW

Engine-neutral foundation of the Flow domain: process AST, runtime contracts, persisted entities, shared token helpers, and the process-builder DSL. Consumed by both engines (default `Schemata.Flow.StateMachine`, full BPMN via `Schemata.Flow.Bpmn`) and by `Schemata.Flow.Foundation`.

## STRUCTURE

```
src/Schemata.Flow.Skeleton/
  Models/         process AST: ProcessDefinition, ProcessSnapshot, ProcessConfiguration, TargetState, TransitionKind, TokenSnapshot
                  gateways: Exclusive / Parallel / Inclusive / Complex / EventBased
                  events: StartEvent, EndEvent, FlowEvent, EventPosition
                  event definitions: IEventDefinition (Message, Signal, Error, Escalation, Timer, Conditional, Cancel, Compensation, Parallel, Link, Multiple, None; MIEventBehavior)
                  tasks: None, Service, User, Manual, BusinessRule, Script, Send, Receive
                  sub-processes: SubProcess (abstract), Embedded, Event, Transaction, CallActivity, AdHoc
                  loops: StandardLoopCharacteristics, MultiInstanceLoopCharacteristics
  Runtime/        contracts + shared helpers
                  IFlowRuntime (Capabilities), FlowRuntimeCapabilities, IFlowEngineValidator, IProcessRegistry, IProcessLifecycleObserver, IFlowTransitionAdvisor, FlowTransitionContext
                  TokenFactory, TokenAggregator, TokenSnapshotFactory, FlowResolver (state-machine engine only)
                  FlowSourceDescriptor, ProcessStates, ISourceCondition, SourceStringConditionExpression, FlowExecutionContext (TouchedSources, LoadedCompensationBindings)
  Entities/       SchemataProcess, SchemataProcessToken, SchemataProcessTransition, SchemataProcessSource, SchemataProcessCompensation
                  token states: Active / Waiting / Completed / Failed / Cancelled / Compensating / Compensated
  Builders/       engine-neutral fluent DSL: ProcessBuilder, ActivityBehavior, BoundaryCatch, Branch, EventBranch, FlowBranch, InclusiveBranch, InclusiveMerge, ParallelFork, ParallelJoin, StartFlow, FlowSourceBindingBuilder
  Utilities/      ProcessDefinitionExtensions, ProcessScopeMap, ProcessStructureValidator
```

## WHERE TO LOOK

| Task | Location |
|---|---|
| Add a new AST node consumable by both engines | `Models/` |
| Add a runtime contract implemented by engines | `Runtime/IFlowRuntime.cs` and adjacent interfaces |
| Persist a new process row | `Entities/` plus `Schemata.Entity.Repository` wiring |
| Change aggregate token-state semantics | `Runtime/TokenAggregator.cs` (aggregate lives on `SchemataProcess.State`) |
| Add fluent DSL for application code | `Builders/` |
| Detect "needs full BPMN engine" at startup | `FlowDiagnostics.RequiresBpmnEngine` |
| Share engine-neutral scope and graph validation helpers | `Utilities/ProcessScopeMap.cs`, `Utilities/ProcessStructureValidator.cs` |
| Wire a CallActivity target | `Runtime/IProcessRegistry.cs`, `ProcessRegistration` |
| Declare source bindings and projection | `Models/ProcessDefinition.cs` (`BindSource`), `Models/FlowSourceProjection.cs`, `Builders/FlowSourceBindingBuilder.cs`, `Runtime/FlowSourceDescriptor.cs` |

## CONVENTIONS / GOTCHAS

- **Engine-neutrality is the contract.** BPMN-only runtime concepts, such as compensation stacks and the coordinator, belong in `Schemata.Flow.Bpmn`, never in `Models/` or `Runtime/`. Compensation *bindings* are the deliberate exception: they are engine-neutral persisted state carried on `ProcessSnapshot.CompensationBindings` and stored per process in `SchemataProcessCompensations`, restored into `FlowExecutionContext` on load, so engines hold no compensation state of their own. New types added here must be representable by the default state-machine engine OR gated behind `FlowDiagnostics.RequiresBpmnEngine` / capability validation.
- **Engine capabilities are declared, not assumed.** `IFlowRuntime.Capabilities` advertises a `FlowRuntimeCapabilities` flag set (`ProcedureTasks`, `MultiToken`, `NestedEvents`, `NestedTimers`, `Compensation`, `SubProcesses`, `Loops`, `NonInterruptingBoundaries`). The state-machine engine declares `ProcedureTasks` only; the BPMN engine declares `All`. `ProcessRegistry` (Foundation) validates each definition against the selected engine's flags and the activated bridges at registration time and throws on the first unsupported shape; inert AST (`AdHocSubProcess`, `LinkDefinition`, `MultipleDefinition`) is rejected by both engine validators instead of being silently executed.
- **ProcedureTask executes on both engines.** DSL `OnEnter`/`OnLeave` synthesize `ProcedureTaskBase`. Each engine builds a `FlowTaskContext` (definition, process, token, execution context, payload), awaits `InvokeAsync`, then resolves the outgoing auto-flow; with no resolvable auto-flow the token parks at the procedure name. StateMachine is the reference semantics; BPMN mirrors it point for point.
- **Event subscriptions are token-scoped.** `SchemataEventSubscription.Token` records the armed token identity: message catches subscribe per token (nested catches and boundary events on nested hosts included — arming walks `definition.AllElements`), while signals stay process-level broadcasts (`Token = null`). Correlation routes the subscription's token into the runner, so two tokens waiting on the same message correlate deterministically.
- **The per-token lifecycle path is deleted.** Fork/join/cancel observer hooks and their events no longer exist; `IProcessLifecycleObserver` (post-commit, process-level) is the only lifecycle notification surface.
- **IFlowRuntime purity.** Engines never load or persist state (root AGENTS.md); XML remarks on `IFlowRuntime.cs` carry the detail.
- **Multi-token AST is not multi-token runtime.** Parallel / inclusive / complex gateways, sub-processes, and MI loops are AST-representable here; the default state-machine engine is a strict subset. Execute them via `Schemata.Flow.Bpmn`.
- **State-machine resolver lives here.** `FlowResolver` is consumed by the state-machine engine only. BPMN has its own `ResolveTargetAsync`. Every resolve/condition entry point requires the execution scope's `IServiceProvider`; `FlowConditionContext.Execution` is `required`, so condition and task contexts always observe the same provider as advisors.
- **Token states are closed.** Adding a new `SchemataProcessTokenState` requires updating `TokenAggregator.ApplyResolvedToToken` and the persistence path in lockstep.
- **String conditions compile at registration.** `SourceStringConditionExpression` carries raw expression text; `ProcessRegistry` (Foundation) binds the predicate via the keyed `IExpressionCompiler` selected by `ProcessConfiguration.Language`. Running an unregistered definition leaves them uncompiled and `Evaluate` throws.
- **CallActivity wiring.** Engines resolve the target at runtime via `IProcessRegistry` / `ProcessRegistration`.
- **Dependency.** `Schemata.Entity.Repository` (entities only). No engine package references, and no owner-package reference — owner-query suppression for source-entity loads is applied by `Schemata.Flow.Foundation` when it creates the persistence scope (`QueryOwnerSuppressed` on the joined repositories' advice contexts).
