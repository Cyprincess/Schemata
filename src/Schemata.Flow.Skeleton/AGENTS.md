# Schemata.Flow.Skeleton

BPMN 2.0.2 AST + a strongly-typed C# DSL for building process graphs. **Contracts only** - runtimes plug in via keyed `IFlowRuntime` (`Schemata.Flow.StateMachine` ships the default subset engine).

## Layout

```
Builders/        ProcessBuilder + DSL primitives (ActivityBehavior, BoundaryCatch,
                 EventBranch, FlowBranch, InclusiveBranch, InclusiveMerge,
                 ParallelFork, ParallelJoin, StartFlow)
Entities/        Persistent process-instance and execution records
Events/          Domain events emitted across flow boundaries
Models/          BPMN element model types
Observers/       Post-commit observer contracts
Runtime/         IFlowRuntime, IFlowEngine, runtime registration contracts
Utilities/       BPMN traversal and validation helpers
```

## Engine Subsetting

The full BPMN AST here accepts: one start event, one+ end events, plain activities, `ExclusiveGateway`, `EventBasedGateway`, `ParallelGateway`, `InclusiveGateway`, `ComplexGateway`, subprocesses, multi-instance loops, interrupting/non-interrupting boundary events, intermediate catch events, message/signal/timer events.

The default `StateMachineEngine` in `Schemata.Flow.StateMachine` accepts only a **subset**: one start, one+ ends, plain activities (no `SubProcess` / `CallActivity` / loop characteristics), `ExclusiveGateway`, `EventBasedGateway` in exclusive mode only, interrupting boundary events, and catch events reachable from an `EventBasedGateway`.

If you author a process that uses parallel/inclusive/complex gateways or multi-instance loops, you need an alternate `IFlowRuntime`.

## Bridges (lives outside this project, but stitched here)

- `Schemata.Flow.Event` correlates `Message` and `Signal` catches with `Schemata.Event.Foundation` (slot 480_300_000)
- `Schemata.Flow.Scheduling` fires `Timer` catches through `Schemata.Scheduling.Foundation` (slot 480_400_000)
- `Schemata.Flow.Http` / `Schemata.Flow.Grpc` expose process instances and transitions

Cross reference: [README.md#features](file:///D:/source/repos/Cyprin/Schemata/README.md).

## Rules

- Pre-commit advisors on a transition **abort** persistence on exception. Post-commit observers log and swallow.
- Use the DSL (`ProcessBuilder`, `Activity`, `EventBranch`, `BoundaryCatch`, ...) to build graphs. Do not construct AST nodes manually - the DSL is the validation gate.
- `IFlowRuntime` is keyed; default key is `SchemataConstants.FlowEngines.StateMachine`. Do not register a runtime under that key from outside `Schemata.Flow.StateMachine`.
- The Skeleton must remain runtime-free. Engine implementations belong in sibling packages.
