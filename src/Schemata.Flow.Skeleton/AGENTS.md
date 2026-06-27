# Schemata.Flow.Skeleton — BPMN AST + Builder DSL

The Schemata BPMN model. Defines the AST that every flow engine consumes, the strongly-typed C# DSL that produces it, and the contracts (`IFlowRuntime`, observers, events) for plugging in an engine. 81 source files.

The reference engine `Schemata.Flow.StateMachine` runs a subset of [BPMN 2.0.2](https://www.omg.org/spec/BPMN/2.0.2/); this package's AST covers more (parallel / inclusive / complex gateways, subprocesses, multi-instance loops) and is intended for alternate engines plugged in via a keyed `IFlowRuntime`.

## Layout

```
Schemata.Flow.Skeleton/
├── SchemataFlowOptions.cs       # framework-wide flow options
├── FlowProcessAuthorization.cs  # per-process authorization contract
├── Builders/                    # strongly-typed C# DSL → AST
├── Entities/                    # process / instance / token persistence models
├── Events/                      # flow lifecycle event payloads (start, complete, error, ...)
├── Models/                      # AST nodes (Activity, Gateway, Event, BoundaryEvent, ...)
├── Observers/                   # IFlowObserver contracts
├── Runtime/                     # IFlowRuntime + engine resolution
└── Utilities/                   # AST traversal + ID assignment helpers
```

## Builder DSL

All in [Builders/](Builders/). The DSL produces the AST in [Models/](Models/):

| File | Role |
|---|---|
| [Builders/ProcessBuilder.cs](Builders/ProcessBuilder.cs) | top-level entry; defines one process |
| [Builders/StartFlow.cs](Builders/StartFlow.cs) | start event + initial sequence flow |
| [Builders/ActivityBehavior.cs](Builders/ActivityBehavior.cs) | activity attachment + execution behaviour binding |
| [Builders/BoundaryCatch.cs](Builders/BoundaryCatch.cs) | interrupting/non-interrupting boundary events |
| [Builders/EventBranch.cs](Builders/EventBranch.cs) | event-based gateway branches |
| [Builders/FlowBranch.cs](Builders/FlowBranch.cs) | exclusive-gateway branches |
| [Builders/InclusiveBranch.cs](Builders/InclusiveBranch.cs) | inclusive-gateway branches |
| [Builders/InclusiveMerge.cs](Builders/InclusiveMerge.cs) | inclusive-gateway merge node |
| [Builders/ParallelFork.cs](Builders/ParallelFork.cs) | parallel-gateway split |
| [Builders/ParallelJoin.cs](Builders/ParallelJoin.cs) | parallel-gateway join |
| [Builders/Branch.cs](Builders/Branch.cs) | shared branch base class |

## Runtime Hook

`IFlowRuntime` (in [Runtime/](Runtime/)) is registered **keyed by engine name** in DI. The default key resolves to `Schemata.Flow.StateMachine`. To plug in an alternate engine, register an `IFlowRuntime` under a different key and reference that key from `SchemataFlowBuilder` in `Schemata.Flow.Foundation`.

Bridges to other subsystems live in companion packages:

- [Schemata.Flow.Event](../Schemata.Flow.Event/) — wires BPMN `Message` and `Signal` intermediate catches to [Schemata.Event.Foundation](../Schemata.Event.Foundation/).
- [Schemata.Flow.Scheduling](../Schemata.Flow.Scheduling/) — wires BPMN `Timer` catches to [Schemata.Scheduling.Foundation](../Schemata.Scheduling.Foundation/).
- [Schemata.Flow.Http](../Schemata.Flow.Http/) / [Schemata.Flow.Grpc](../Schemata.Flow.Grpc/) — resource bridges for process instances and transitions.

## Conventions

- **AST nodes are immutable records**. Build them through the DSL — never `new` an AST node directly outside `Builders/`.
- **IDs**: builder assigns deterministic node IDs via [Utilities/](Utilities/). Do not hand-write IDs; bridging to `Event`/`Scheduling` depends on the assigned shape.
- **Observers**: register `IFlowObserver` implementations through DI. Observer exceptions are logged at `Warning` and swallowed — they cannot fail a transition ([docs/documents/scheduling/jobs.md:186-189](../../docs/documents/scheduling/jobs.md)).
- **Engine subset**: when authoring a process for the default `StateMachine` engine, stay within: one start event, ≥1 end event, plain activities (no `SubProcess` / `CallActivity` / loop characteristics), `ExclusiveGateway`, `EventBasedGateway` (exclusive mode), interrupting boundary events, intermediate catches reachable from an `EventBasedGateway`. Wider AST features require a custom `IFlowRuntime`.

## Anti-Patterns

- **Do NOT** add ASP.NET / HTTP dependencies to this package — Skeleton stays transport-agnostic. Transport adapters live in `Flow.Http` and `Flow.Grpc`.
- **Do NOT** mutate an AST after `ProcessBuilder.Build()` — observers, persistence, and runtime cache it.
- **Do NOT** assume `Timer` / `Message` / `Signal` catches will fire without their bridge package. They are inert until `Flow.Scheduling` / `Flow.Event` is added.
- **Do NOT** plug an engine in directly with `services.AddSingleton<IFlowRuntime>` — use keyed registration so multiple engines can coexist.
- **Do NOT** call `BoundaryCatch` / `EventBranch` / `BoundaryCatch` from outside their parent builder. The DSL relies on parent-child threading to produce correct AST IDs.

## Notes

- The C# DSL is intentionally verbose where BPMN is ambiguous (e.g. `ParallelFork` + `ParallelJoin` rather than letting a gateway play both roles). That is by design.
- `FlowProcessAuthorization` is the per-process authorization plug-in; pair with `Schemata.Security.Foundation` for policy evaluation.
- Tests: [../../tests/Schemata.Flow.Tests/](../../tests/Schemata.Flow.Tests/).
- For the alternate-engine plug-in pattern, see `Schemata.Flow.StateMachine` which is the in-tree reference implementation against this Skeleton.
