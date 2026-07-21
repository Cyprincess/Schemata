# BPMN Engine

`Schemata.Flow.Bpmn` is the multi-token Flow engine for executable BPMN process definitions. It
implements `IFlowRuntime` under the keyed engine name `bpmn`, while the default `StateMachineEngine`
remains the single-token engine wired by `UseFlow()`. The BPMN engine is the right choice when a
process needs parallel or inclusive gateways, sub-process scopes, call activity spawns, loop
characteristics, non-interrupting boundary paths, escalation, transaction cancel, or compensation.
The registration and engine key live in `src/Schemata.Flow.Bpmn/Features/SchemataFlowBpmnFeature.cs`
and `src/Schemata.Flow.Bpmn/BpmnEngine.cs`.

The engine uses the shared Flow AST described in [AST Reference](ast.md) and the same runtime
persistence path described in [Runtime Services](runtime.md). The difference is the position model:
`StateMachineEngine` keeps one `SchemataProcessToken`, while `BpmnEngine` can create, wait, cancel,
join, and compensate many tokens in one process instance.

## Activating the engine

Add a direct package reference to `Schemata.Flow.Bpmn`, then call `UseBpmn()` on the
`SchemataFlowBuilder` returned by `UseFlow()`. The extension is defined in
`src/Schemata.Flow.Bpmn/Extensions/FlowBpmnBuilderExtensions.cs`; it adds
`SchemataFlowBpmnFeature`, which depends on `SchemataFlowFeature` and registers the BPMN runtime and
validator under `SchemataConstants.FlowEngines.Bpmn`.

```text
schema.UseFlow()
      .UseBpmn()
      .Use<OrderProcess>(engine: "bpmn");
```

`Schemata.Flow.Bpmn` is intentionally not pulled in by the Flow meta-target packages. A consuming
application references it explicitly when it needs BPMN execution. The feature registration in
`src/Schemata.Flow.Bpmn/Features/SchemataFlowBpmnFeature.cs` uses keyed services:

- `IFlowRuntime` -> `BpmnEngine`
- `IFlowEngineValidator` -> `BpmnFlowEngineValidator`

The default StateMachine engine continues to serve process registrations that omit an engine name or
choose `statemachine`. See [State Machine](state-machine.md) for that engine.

## Feature matrix

The support levels below are based on `src/Schemata.Flow.Bpmn/BpmnEngine.cs`,
`src/Schemata.Flow.Bpmn/BpmnValidator.cs`, and the executors under
`src/Schemata.Flow.Bpmn/Runtime/`. `Full` means the engine has a runtime path for the element in the
current executable subset. `Subset` means the element class is supported with explicit limits.
`Not supported` means the engine, validator, or conformance coverage matrix excludes it.

| Element                                                                | Category      | Support level | Runtime path                                                                                                                                                     |
| ---------------------------------------------------------------------- | ------------- | ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| None start event                                                       | Event         | Full          | `StartAsync` falls through to `ResolveTargetAsync` for the start's outgoing flow.                                                                                |
| Message / Signal / Conditional start event                             | Event         | Subset        | `StartAsync` falls through; broadcast correlation belongs to the event bridge.                                                                                   |
| Timer start event                                                      | Event         | Not supported | The AST shape is not modeled as a start scheduler; timer starts are outside the executable subset.                                                               |
| None end event                                                         | Event         | Full          | `ResolveTargetAsync` returns `IsComplete=true` for `FlowEvent { Position: End }`.                                                                                |
| Terminate end event                                                    | Event         | Full          | `TerminateScopeAsync` cancels sibling tokens in the same scope; root terminate sets `process.State="Terminated"`, scoped terminate resumes the parent scope.     |
| Error end event                                                        | Event         | Subset        | Failure routing reuses `EscalationBoundaryHandler.TryFireErrorBoundaryAsync`.                                                                                    |
| Escalation end event                                                   | Event         | Full          | `EscalationBoundaryHandler.ThrowEndAsync` propagates through scope boundaries.                                                                                   |
| Cancel end event                                                       | Event         | Full          | `TransactionExecutor.TryHandleCancelEndAsync` handles cancel-end compensation and boundary activation.                                                           |
| Compensation end event                                                 | Event         | Full          | `ThrowEndCompensationAsync` invokes `CompensationThrowHandler.FireForEngineAsync`.                                                                               |
| Intermediate catch event                                               | Event         | Full          | `TriggerIntermediateCatchAsync` resumes a waiting token when `FlowEventMatcher.Matches` accepts the trigger.                                                                 |
| Intermediate throw event                                               | Event         | Subset        | Compensation and escalation throw definitions execute; other throw definitions are model-only.                                                                   |
| Boundary event, interrupting                                           | Event         | Full          | `FireBoundaryAsync` cancels the host token and routes a replacement token.                                                                                       |
| Boundary event, non-interrupting                                       | Event         | Full          | `NonInterruptingBoundaryHandler` spawns a sibling token and leaves the host live.                                                                                |
| Error boundary event                                                   | Event         | Full          | `FireBoundaryAsync` plus `EscalationBoundaryHandler.TryFireErrorBoundaryAsync`.                                                                                  |
| Escalation boundary event                                              | Event         | Full          | `BoundaryCatch` defaults `Interrupting = false` for `EscalationDefinition`; `EscalationBoundaryHandler` bubbles throws through scope boundaries.                 |
| Timer boundary event                                                   | Event         | Full          | Engine routes the token; `AdviceTransitionTimer` provisions the scheduler wake-up.                                                                               |
| Message boundary event                                                 | Event         | Full          | Engine routes the token; `AdviceTransitionEvent` provisions the subscription.                                                                                    |
| Signal boundary event                                                  | Event         | Full          | Same routing as message; `FlowEventHandler` dispatches matching signals.                                                                                         |
| Conditional boundary event                                             | Event         | Full          | Boundary matching uses `FlowEventMatcher.Matches`.                                                                                                                |
| Compensation boundary event                                            | Event         | Full          | `RegisterCompensationBoundaries` records handlers through `CompensationBoundaryHandler.RegisterAll`.                                                             |
| Cancel boundary event                                                  | Event         | Subset        | `BpmnValidator.ValidateCancelBoundaries` requires the host to be a `TransactionSubProcess`.                                                                      |
| Link event definition                                                  | Event         | Not supported | AST shape accepted; no runtime executor.                                                                                                                         |
| Multiple event definition                                              | Event         | Not supported | `FlowEventMatcher.Matches` matches single definitions only.                                                                                                       |
| Parallel event definition                                              | Event         | Subset        | `EventBasedGateway.Parallel` mode forks one child per matched catch; arbitrary parallel catch events are outside the subset.                                     |
| None task                                                              | Task          | Full          | `BpmnEngine.ResolveTargetAsync` returns the activity as the next state.                                                                                          |
| Service / User / Send / Receive / Script / Manual / Business rule task | Task          | Full          | Same `ResolveTargetAsync` activity case; task subtype is engine-neutral.                                                                                         |
| Exclusive gateway                                                      | Gateway       | Full          | `BpmnEngine.ResolveExclusiveAsync` evaluates guarded flows in order, falls back to default.                                                                      |
| Parallel gateway, fork                                                 | Gateway       | Full          | `ParallelGatewayHandler.ForkFromTokenAsync` splits the arriving token; `BpmnEngine.SpawnFromGatewayAsync` is the engine-level helper.                            |
| Parallel gateway, join                                                 | Gateway       | Full          | `ParallelGatewayHandler.ArriveAtJoinAsync` parks until `waitingHere.Count + 1 == incomingCount`, then `BpmnEngine.FireJoinAsync` fires.                          |
| Inclusive gateway, branch                                              | Gateway       | Full          | `InclusiveGatewayHandler.BranchFromTokenAsync` evaluates conditions; `BpmnEngine.ResolveInclusiveBranchesAsync` selects matched flows plus default fallback.     |
| Inclusive gateway, join                                                | Gateway       | Full          | `InclusiveGatewayHandler.ArriveAtJoinAsync` parks while `HasLiveUpstreamReachableTo` is true, otherwise fires.                                                   |
| Event-based gateway, exclusive                                         | Gateway       | Full          | `EventBasedGatewayHandler.TriggerAsync` calls `BpmnEngine.RouteSingleEventBasedAsync` to route the existing token.                                               |
| Event-based gateway, parallel                                          | Gateway       | Full          | `EventBasedGatewayHandler.TriggerAsync` calls `BpmnEngine.SpawnFromEventBasedAsync` to fork one child per matched branch.                                        |
| Complex gateway                                                        | Gateway       | Subset        | `ComplexGatewayHandler.FromTokenAsync` honors an optional `ActivationCount`; absent that, falls back to inclusive-gateway behavior.                              |
| Embedded sub-process                                                   | SubProcess    | Full          | `BpmnEngine.SpawnSubProcessChildAsync` parks the parent and spawns a child token in the nested scope.                                                            |
| Event sub-process                                                      | SubProcess    | Full          | `EventSubProcessExecutor.TryFireAsync` handles interrupting and non-interrupting triggers.                                                                       |
| Transaction sub-process                                                | SubProcess    | Full          | `TransactionExecutor.TryHandleCancelEndAsync` handles entry, cancel-end compensation, cancel-boundary activation, and scope cleanup.                             |
| Ad hoc sub-process                                                     | SubProcess    | Not supported | AST shape accepted; no executor dispatches on it.                                                                                                                |
| Call activity                                                          | SubProcess    | Subset        | `CallActivityExecutor` resolves the target via `IProcessRegistry`, spawns a same-registry child process, and resumes the parent when the child ends.             |
| Standard loop                                                          | Loop          | Full          | `StandardLoopExecutor` implements test-before and test-after while/do-while semantics.                                                                           |
| Multi-instance loop                                                    | Loop          | Subset        | `MultiInstanceExecutor` supports sequential and parallel cardinality; `ValidateBehavior` throws `NotSupportedException` for `MIEventBehavior.One` or `.Complex`. |
| Collaboration                                                          | Collaboration | Not supported | Multi-participant collaboration is outside the single-process engine scope.                                                                                      |
| Pool                                                                   | Collaboration | Not supported | Pool and participant modeling are outside the engine scope.                                                                                                      |
| Lane                                                                   | Collaboration | Not supported | Lane modeling is outside the engine scope.                                                                                                                       |
| Data object / I/O metadata                                             | Data          | Not supported | Data object and I/O specification are non-executable and outside the engine scope.                                                                               |
| Text annotation / association                                          | Annotation    | Not supported | Non-executable notation is outside runtime execution.                                                                                                            |

## Token lifecycle

The BPMN engine returns a `ProcessSnapshot` from every `IFlowRuntime` call. The snapshot contains the
updated `SchemataProcess`, the full live token set, and the transition rows to persist. The state
values come from `src/Schemata.Flow.Skeleton/Entities/SchemataProcessToken.cs` and the aggregate
state rules come from `BpmnEngine.ApplyAggregateState`.

| Token state    | Meaning                                                                                               | Engine source                                                                                                                                                |
| -------------- | ----------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Active`       | The token sits on an executable activity or a gateway that can auto-advance.                          | `TokenStateFor` returns `Active` when the target is not complete and not waiting.                                                                            |
| `Waiting`      | The token is parked at an event catch, event-based gateway, sub-process, call activity, or join.      | `ParkAtGateway`, `StartIntoEventBased`, and sub-process entry set `WaitingAtName`.                                                                           |
| `Cancelled`    | The token was interrupted by a boundary event, transaction cancel, or explicit cancellation.          | `FireBoundaryAsync` and `TransactionExecutor.TryHandleCancelEndAsync` set cancelled tokens.                                                                  |
| `Failed`       | The token failed and can still be counted by join handling when the workflow permits failed-continue. | `TokenStates.JoinCounted` includes `Failed`; failure routing sets token state.                                                                               |
| `Completed`    | The token reached an end event or was consumed by a fork/join/merge.                                  | `ResolveTargetAsync`, `BranchFromTokenAsync`, and `FireJoinAsync`.                                                                                           |
| `Compensating` | Reserved for a compensation operation that is in progress.                                            | `SchemataProcessToken.State` comment lists the value; compensation runtime currently emits `TransitionKind.Compensate` rather than flipping the token state. |
| `Compensated`  | Reserved for a compensation operation that completed.                                                 | `TokenStates.Terminal` includes `Compensated`; the BPMN engine does not currently write this value on the token row.                                         |

Transition kinds on `SchemataProcessTransition` describe why a token row changed. The BPMN engine
creates them in `BpmnEngine.NewTransition`:

| Kind         | Produced when                                                                                                    |
| ------------ | ---------------------------------------------------------------------------------------------------------------- |
| `Move`       | A token advances across a sequence flow, arrives at a gateway, resumes from a catch, or exits a scope.           |
| `Fork`       | A parallel fork, inclusive branch, or parallel event-based gateway consumes one token and opens child paths.     |
| `Join`       | A parallel or inclusive join consumes waiting inputs and creates one output token.                               |
| `Spawn`      | A sub-process, call activity, event sub-process, or non-interrupting boundary path creates a child token.        |
| `Cancel`     | An interrupting boundary, cancel-end, terminate-end, or scope cancel removes a live token from normal execution. |
| `Fail`       | A child call activity or handler failure marks a token failed.                                                   |
| `Compensate` | A compensation handler runs for a completed activity.                                                            |

A compact repository query can inspect the current token set:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;

public static class FlowTokenQueries
{
    public static async IAsyncEnumerable<SchemataProcessToken> ListLiveTokensAsync(
        IRepository<SchemataProcessToken> tokens,
        string process,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var token in tokens.ListAsync(
            q => q.Where(t => t.Process == process && t.DeleteTime == null), ct))
        {
            yield return token;
        }
    }
}
```

Persisting snapshots is handled by Flow Foundation. Consumers normally inspect tokens through the
HTTP or gRPC resource surface, or through repositories inside application services.

### Waiting position and StateName

When a token parks at an event-based gateway, `StateName` keeps the business node the token
arrived from; `EventBasedGatewayHandler.ArriveAtGateway` sets it to the arriving token's previous
state, and the gateway name goes to `WaitingAtName`, the lookup key trigger dispatch uses. The one
exception is a process that starts directly into an event-based gateway (`StartIntoEventBased`):
with no preceding activity, `StateName` holds the gateway name. The state-machine engine follows
the same contract (see [Engine](engine.md)), so a bound source projects the business node name
rather than the synthetic `Await_*` gateway name on either engine.

## Compensation semantics

Compensation is implemented inside `src/Schemata.Flow.Bpmn/Runtime/Compensation/` and coordinated by
`BpmnEngine` methods such as `RegisterCompensationBoundaries`, `ThrowIntermediateCompensationAsync`,
and `ThrowEndCompensationAsync`. This section summarizes the consumer-visible behavior.

A completed activity with a compensation boundary registers a deferred handler in the current
scope's `CompensationStack`. Registration does not run the handler. A compensation throw activates
the stack: a targeted throw selects the most recent handler for the referenced activity, while a
global throw runs all eligible handlers in reverse registration order. The reverse order also
applies when a `TransactionSubProcess` reaches a cancel end event.

If a compensation handler fails, already completed handlers stay compensated. The failure routes
through the same boundary error path used by other BPMN failures; it does not mark the compensation
operation complete. `ICompensationLifecycleObserver` receives start notifications per handler and a
completion notification only after the whole compensation operation succeeds.

Compensation registrations are persisted, not engine memory. Each persist replaces the process's
rows in the `SchemataProcessCompensations` table (see
[Runtime Services — Persisted compensation bindings](runtime.md#persisted-compensation-bindings));
on load the engine rehydrates them from `FlowExecutionContext.LoadedCompensationBindings`, so a
compensation throw after a host restart still resolves its handlers.

## Extension points

- `IFlowRuntime` is keyed by `SchemataConstants.FlowEngines.Bpmn` (`"bpmn"`), so multiple engines can
  coexist in one host.
- `IFlowEngineValidator` lets the BPMN package validate structure during process registration.
- `IConditionExpression` supplies asynchronous guards for exclusive and inclusive gateway branches.
- `IProcessLifecycleObserver` observes process-level start, transition, termination, and failure.
  It is the only lifecycle observer interface; the per-token observer path was removed.
- `ICompensationLifecycleObserver` observes BPMN-only compensation start and completion.

The concrete contracts live in `src/Schemata.Flow.Skeleton/Runtime/`. The
`ICompensationLifecycleObserver` interface is shipped inside the BPMN package at
`src/Schemata.Flow.Bpmn/Runtime/Compensation/` because compensation stacks are engine-specific.

## Bridges

`Schemata.Flow.Event` provisions message and signal wake-up infrastructure. Its
`AdviceTransitionEvent` runs before a Flow transition commits and keeps event subscriptions aligned
with the new waiting state; `FlowEventHandler` later calls into `FlowRunner` (via
`CorrelateMessageHandler` and `ThrowSignalHandler`) when a matching event is dispatched. See
[Flow Event Integration](event.md).

`Schemata.Flow.Scheduling` provisions timer wake-ups. Its `AdviceTransitionTimer` schedules or
cancels one-shot timer jobs for timer catches; the timer job invokes `FlowRunner.CompleteAsync` (or
the matching handler in the resource bridge) when the timer fires. See
[Flow Scheduling Integration](scheduling.md).

The BPMN engine owns token routing once the trigger reaches it. The bridge packages own external
subscription and scheduler state.

## Limits and out-of-scope features

The BPMN engine executes a defined subset of BPMN 2.0.2; diagram interchange is out of scope. These
elements are explicitly outside scope in the runtime code:

- Collaboration, participants, pools, and lanes.
- `AdHocSubProcess`.
- `LinkDefinition`, `MultipleDefinition`, and arbitrary `ParallelDefinition` event matching outside
  event-based gateway mode.
- Data objects, I/O specification metadata, properties, text annotations, and associations.
- Cross-scope inclusive merges where an upstream token in another scope can satisfy a join.
- A combined inclusive join-and-split gateway. `AdvanceAsync` throws
  `BPMN_TRANSPARENT_GATEWAY_NOT_SUPPORTED` when both `Incoming.Count > 1` and
  `Outgoing.Count > 1` on a parallel or inclusive gateway; the author must split it into separate
  join and split nodes.
- Complex-gateway `ActivationCount` semantics. The handler ignores the activation count when it is
  unset and falls back to inclusive-gateway branching.

## Compliance

The MIWG conformance suite runs an executable subset of the spec. Vectors are loaded from
`specs/bpmn-miwg-test-suite`, executed as `Theory` tests by
`tests/Schemata.Flow.Bpmn.Conformance.Tests/BpmnConformanceShould.cs`, and recorded as passing or
pending. The exclusion source of truth is `PendingCatalog.cs`; each entry pairs a vector path with
a reason. Reasons observed across the catalog include:

- `Initial conformance execution failed; vector outside current executable subset`
- `Collaboration not supported (multi-participant)`
- `Data object modeling is outside engine scope`
- `Associations are non-executable and outside engine scope`
- `Text annotations are non-executable and outside engine scope`

A vector that throws during parse, validation, or execution with no cataloged reason surfaces as a
test failure through `FailUncatalogued`.

## See also

- [Flow overview](overview.md)
- [DSL Reference](dsl.md)
- [AST Reference](ast.md)
- [Runtime Services](runtime.md)
- [State Machine](state-machine.md)
- [BPMN Inclusive Merge](bpmn-inclusive-merge.md)
