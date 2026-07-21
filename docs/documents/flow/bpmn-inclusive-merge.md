# BPMN Inclusive Merge

An `InclusiveGateway` can split execution into one or more branches and later merge only the branches
that were actually taken. The merge is subtle because a join with three incoming flows may need to
fire after one, two, or three arrivals. Waiting for every inbound sequence flow would deadlock
whenever an upstream condition did not select a branch.

Schemata implements a subset behavior consistent with BPMN 2.0.2 section 10.6.4: an inclusive merge
waits while a live token can still reach the merge, then fires when the remaining inbound paths are
dead. The source of truth is `src/Schemata.Flow.Bpmn/BpmnEngine.cs`, especially
`ResolveInclusiveBranchesAsync`, `HasLiveUpstreamReachableTo`, `FireJoinAsync`,
`BranchFromTokenAsync`, `SpawnFromGatewayAsync`, and the `InclusiveGatewayHandler` static methods.

## Semantic vocabulary

- **Branch** — one outgoing `SequenceFlow` from an inclusive split. Branches are selected by
  `BpmnEngine.ResolveInclusiveBranchesAsync`.
- **Dead path** — an inbound path to the merge with no live token that can still arrive. Schemata
  does not create placeholder dead-path tokens.
- **Live token** — a `SchemataProcessToken` whose `State` is in `TokenStates.Live` (`Active` or
  `Waiting`). It is a candidate for `HasLiveUpstreamReachableTo`.
- **Join-counted token** — a token whose `State` is in `TokenStates.JoinCounted` (`Waiting` or
  `Failed`). Siblings already parked at the merge count as arrivals.
- **Join** — an `InclusiveGateway` with more than one incoming flow. `BpmnEngine.IsJoin` detects this
  shape by counting flows whose target is the gateway.
- **Condition** — an `IConditionExpression` on a branch flow. It evaluates against
  `FlowConditionContext` built by `BpmnEngine.BuildConditionContext`.
- **Default flow** — a branch marked `SequenceFlow.IsDefault`. It is selected only when no guarded or
  unguarded non-default branch was selected.

These terms match the runtime code rather than diagram notation.

## The dead-path problem

A naive merge rule says: "wait until one token has arrived on every incoming flow." That rule works
for a parallel join because every outgoing path from a parallel fork is taken. It fails for an
inclusive join because the split chooses a non-empty subset of outgoing paths.

```
        [Split OR]
        /    |    \
      A      B     C
       \     |    /
        [Merge OR]
             |
           After
```

If the split selected A and B but not C, the merge has three incoming flows and only two possible
arrivals. Waiting for C would park the process forever. If the split selected only A, the merge
must fire after A arrives. If A arrives while B is still active and can reach the merge, A must
wait.

Schemata solves this by looking at live tokens, not only gateway degree. A path is dead when no live
token outside the arriving token can reach the merge in the current process definition.

## Engine algorithm

The runtime has two halves: branch selection at the inclusive split, and dead-path detection at
the inclusive merge.

### Branch selection

`InclusiveGatewayHandler.BranchFromTokenAsync` handles an `InclusiveGateway` with more than one
outgoing flow in `src/Schemata.Flow.Bpmn/Runtime/Gateways/InclusiveGatewayHandler.cs`. It calls
`BpmnEngine.ResolveInclusiveBranchesAsync`, which evaluates outgoing flows in definition order:

```text
matched = []
defaultFlow = null

for each outgoing flow from the inclusive gateway:
    if flow.IsDefault:
        defaultFlow = flow
        continue

    if flow.Condition is null:
        matched.add(flow)
        continue

    if await flow.Condition.Evaluate(context):
        matched.add(flow)

if matched is empty and defaultFlow exists:
    matched.add(defaultFlow)

return matched
```

If no branch matches and no default exists, the handler throws `FailedPreconditionException`
(`STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING`). Otherwise it calls
`BpmnEngine.BranchFromTokenAsync`, marks the original token `Completed` at the split, and creates
one child token per selected branch.

The start path is `InclusiveGatewayHandler.StartIntoBranchAsync`, which delegates to
`BpmnEngine.SpawnFromGatewayAsync` to spawn the children without a parent-to-child fork row.

### Merge decision

`InclusiveGatewayHandler.ArriveAtJoinAsync` handles an `InclusiveGateway` with more than one
incoming flow. The method first collects sibling tokens already parked at the same gateway and
counted by join logic: `Waiting` and `Failed` states are included through
`TokenStates.JoinCounted`.

It then calls `BpmnEngine.HasLiveUpstreamReachableTo(definition, ig, working, token)`. That helper:

1. Finds live tokens other than the arriving token (`!ReferenceEquals(t, token)`).
2. Keeps only tokens whose `State` is in `TokenStates.Live` (`Active` or `Waiting`).
3. Excludes tokens already parked at the same gateway (`t.StateName != gateway.Name`).
4. Runs `BpmnEngine.CanReach` from each token's current `StateName` through outgoing sequence flows,
   walking the process definition graph.
5. Returns `true` if any live token can reach the inclusive merge.

When `HasLiveUpstreamReachableTo` returns `true`, `ArriveAtJoinAsync` parks the arriving token at
the merge by calling `BpmnEngine.ParkAtGateway`, which sets `State = "Waiting"` and sets
`WaitingAtName` to the gateway name. When it returns `false`, the method calls
`BpmnEngine.FireJoinAsync` with the arriving token plus the captured waiting siblings.
`FireJoinAsync` marks the inputs `Completed`, follows the merge's outgoing flow, creates one output
token, and writes a `TransitionKind.Join` row.

### End-to-end walk

Consider a split with three branches A, B, and C. Conditions select A and B; C is false.

1. A token reaches the split gateway.
2. `ResolveInclusiveBranchesAsync` selects flows to A and B.
3. `BranchFromTokenAsync` completes the split token and creates two active child tokens.
4. Token A advances to the merge first.
5. `HasLiveUpstreamReachableTo` sees token B is live and can reach the merge, so A is parked at the
   merge as `Waiting`.
6. Token B advances to the merge.
7. The helper ignores the arriving B token and the already waiting A token. No other live token can
   reach the merge; C has no token.
8. `FireJoinAsync` completes A and B, creates one output token after the merge, fires the join
   notification, and writes the `TransitionKind.Join` transition row.

## Counter-examples

The examples below describe behavior at the merge. They use compact BPMN notation rather than full
C# process definitions.

### 1. All branches taken

```
Start -> Work -> OR-split -> A -> OR-merge -> After -> End
                      \-> B -/
                      \-> C -/
```

Conditions for A, B, and C are true. The split creates three active child tokens. The first two
arrivals park at the merge because live upstream tokens can still reach it. The third arrival fires
the merge. Expected token count at merge before firing: three inputs. Expected outgoing tokens: one
child token at `After`.

### 2. Some branches dead, merge reachable

```
Start -> Work -> OR-split -> A -> OR-merge -> After -> End
                      \-> B -/
                      \-> C -/   (C condition false)
```

A and B are selected; C is a dead path. The first arrival parks because the other selected branch
is still live. The second arrival fires the merge even though the gateway has three incoming
flows. Expected token count at merge before firing: two inputs. Expected outgoing tokens: one child
token at `After`.

### 3. Fully dead non-default branch set

```
Start -> Work -> OR-split -> Default -> OR-merge -> After -> End
                      \-> A -----/   (A condition false)
                      \-> B -----/   (B condition false)
```

A and B conditions are false, and the default flow is selected. The merge fires after the default
branch arrives because no live token can still reach the merge through A or B. Expected token count
at merge before firing: one input. Expected outgoing tokens: one child token at `After`.

If no branch condition matches and no default flow exists, the split throws before any merge
decision. The guard lives in `InclusiveGatewayHandler.BranchFromTokenAsync` (and in
`StartIntoBranchAsync` for the start path).

### 4. Nested inclusive inside another gateway

```
Start -> Parallel-fork -> Left -> OR-split -> A -> OR-merge -> LeftDone -> Parallel-join
                    \                  \-> B -/                            /
                     \-> Right -------------------------------------------/
```

The inner inclusive merge looks only for live tokens reachable to that inclusive gateway. The right
parallel token is live, but its path reaches the parallel join, not the inner inclusive merge. The
inclusive merge can fire when its selected inner branches arrive. Expected token count at the
inclusive merge: one or two inner inputs, depending on selected conditions. Expected outgoing
tokens: one token at `LeftDone`; the outer parallel join still waits for the right branch.

### 5. Inclusive merge downstream of a parallel fork

```
Start -> Parallel-fork -> A -> OR-merge -> After -> End
                    \-> B -> OR-merge
```

Both parallel branches can reach the same inclusive merge. Even though the merge is inclusive, both
live upstream tokens are reachable to it, so the first arrival parks. The second arrival fires the
merge. Expected token count at merge before firing: two inputs. Expected outgoing tokens: one token
at `After`.

This shape is valid only when the process definition models the gateway as a merge with a single
outgoing flow. `BpmnEngine.AdvanceAsync` rejects a combined inclusive join-and-split gateway with
`BPMN_TRANSPARENT_GATEWAY_NOT_SUPPORTED` when both `Incoming.Count > 1` and
`Outgoing.Count > 1`; the author must split it into separate join and split nodes.

## Limitations

- No cross-scope inclusive merge. `CanReach` walks the process definition graph from token
  `StateName`; it does not let a token in another scope satisfy a merge inside a sub-process scope.
- No combined inclusive join-and-split gateway. `AdvanceAsync` throws and asks the author to split
  the gateway into separate merge and branch nodes.
- ComplexGateway interaction is supported as a subset. `ComplexGatewayHandler.FromTokenAsync`
  honors an optional `ActivationCount`; absent that, it reuses inclusive-gateway condition
  evaluation and branching.
- No special `EventBasedGateway` interaction. Event-based waits are handled by trigger matching
  before normal inclusive merge logic.
- No inactive placeholder tokens. A dead path is inferred from live token reachability, not
  persisted.

## Debugging tips

Inspect `SchemataProcessTransition` rows for the process instance:

- `Kind = Fork` with `Event = Branch` shows the inclusive split.
- `Kind = Move` rows with `Previous = <split name>` show spawned branch tokens.
- `Kind = Move` rows with `Posterior = <merge name>` show arrivals that parked at the merge.
- `Kind = Join` shows the merge fired and created the output token.

Then inspect `SchemataProcessToken` rows. Tokens at the merge with `State = Waiting` are captured
arrivals. Tokens elsewhere with `State = Active` or `Waiting` are candidates for
`HasLiveUpstreamReachableTo`; if their `StateName` can reach the merge through `SequenceFlow`
edges, the next arrival should park instead of firing.

## See also

- [BPMN Engine](bpmn-engine.md)
- [Runtime Services](runtime.md)
- [DSL Reference](dsl.md)
- [AST Reference](ast.md)
