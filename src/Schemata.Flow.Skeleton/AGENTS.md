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
                  IFlowRuntime, IFlowEngineValidator, IProcessRegistry, IProcessLifecycleObserver, ITokenLifecycleObserver, IFlowTransitionAdvisor, FlowTransitionContext
                  TokenFactory, TokenAggregator, TokenSnapshotFactory, FlowResolver (state-machine engine only)
                  FlowSourceDescriptor, ProcessStates, ISourceCondition, SourceStringConditionExpression, FlowExecutionContext (TouchedSources)
  Entities/       SchemataProcess, SchemataProcessToken, SchemataProcessTransition, SchemataProcessSource
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

- **Engine-neutrality is the contract.** BPMN-only runtime concepts, such as compensation stacks, belong in `Schemata.Flow.Bpmn`, never in `Models/` or `Runtime/`. New types added here must be representable by the default state-machine engine OR gated behind `FlowDiagnostics.RequiresBpmnEngine`.
- **IFlowRuntime purity.** Engines never load or persist state (root AGENTS.md); XML remarks on `IFlowRuntime.cs` carry the detail.
- **Multi-token AST is not multi-token runtime.** Parallel / inclusive / complex gateways, sub-processes, and MI loops are AST-representable here; the default state-machine engine is a strict subset. Execute them via `Schemata.Flow.Bpmn`.
- **State-machine resolver lives here.** `FlowResolver` is consumed by the state-machine engine only. BPMN has its own `ResolveTargetAsync`. Every resolve/condition entry point requires the execution scope's `IServiceProvider`; `FlowConditionContext.Execution` is `required`, so condition and task contexts always observe the same provider as advisors.
- **Token states are closed.** Adding a new `SchemataProcessTokenState` requires updating `TokenAggregator.ApplyResolvedToToken` and the persistence path in lockstep.
- **String conditions compile at registration.** `SourceStringConditionExpression` carries raw expression text; `ProcessRegistry` (Foundation) binds the predicate via the keyed `IExpressionCompiler` selected by `ProcessConfiguration.Language`. Running an unregistered definition leaves them uncompiled and `Evaluate` throws.
- **CallActivity wiring.** Engines resolve the target at runtime via `IProcessRegistry` / `ProcessRegistration`.
- **Dependency.** `Schemata.Entity.Repository` (entities only). No engine package references.
