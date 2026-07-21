# Flow Runtime Services

`ProcessRegistry`, `FlowRunner`, and `ProcessPersistence` bridge Flow engines to the rest of the
framework. `ProcessRegistry` holds compiled definitions and resolves the engine for each.
`FlowRunner` loads persisted state, creates a `FlowExecutionContext`, drives the engine, runs the
advisor pipeline inside the transition unit of work, persists the returned snapshot, and notifies
observers. `ProcessPersistence` coordinates process, token, transition, source-binding, and compensation
repositories under one unit of work.

## Where the code lives

| Package                    | Key files                                                                                                                                                                                                                                                                                                                                                                                                                         |
| -------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Flow.Foundation` | `FlowRunner.cs`, `IFlowRunner.cs`, `ProcessRegistry.cs`, `ProcessPersistence.cs`, `ProcessLifecycleNotifier.cs`, `FlowHandlerSupport.cs`, `Advisors/AdviceSourceProjection.cs`, `StartProcessOptions.cs`                                                                                                                                                                                                                                                                               |
| `Schemata.Flow.Skeleton`   | `Runtime/IFlowRuntime.cs`, `Runtime/FlowRuntimeCapabilities.cs`, `Runtime/IProcessRegistry.cs`, `Runtime/IProcessLifecycleObserver.cs`, `Runtime/TokenStates.cs`, `Runtime/FlowSourceDescriptor.cs`, `Runtime/ProcessStates.cs`, `Models/FlowSourceProjection.cs`, `Builders/FlowSourceBindingBuilder.cs`, `Observers/IFlowTransitionAdvisor.cs`, `Observers/IFlowSourceAdvisor.cs`, `Observers/FlowTransitionContext.cs`, `Entities/SchemataProcess.cs`, `Entities/SchemataProcessToken.cs`, `Entities/SchemataProcessSource.cs`, `Entities/SchemataProcessTransition.cs`, `Entities/SchemataProcessCompensation.cs` |

## ProcessRegistry

`ProcessRegistry` implements `IProcessRegistry` and is a **singleton**. It stores registrations in a
`ConcurrentDictionary<string, ProcessRegistration>` keyed case-insensitively by process name.

Registering a configuration:

1. Instantiates the `ProcessDefinition` subclass, running its DSL constructor.
2. Resolves the keyed `IFlowRuntime` for the configuration's engine; a missing engine throws
   `InvalidOperationException`.
3. Validates the definition against the engine's declared `Capabilities` and the activated bridges:
   message/signal catches require `Schemata.Flow.Event`, timer catches require
   `Schemata.Flow.Scheduling`. The first unsupported shape throws `InvalidOperationException` naming
   the shape, the engine, and the missing capability (see [Runtime capabilities](#runtime-capabilities)).
4. Calls `Validate` on every `IFlowEngineValidator` whose `EngineName` matches the configuration's
   engine. Both built-in validators reject inert AST (`AdHocSubProcess`, `LinkDefinition`,
   `MultipleDefinition`) rather than letting it execute with degraded semantics.
5. Compiles every string condition expression with the keyed `IExpressionCompiler` selected by the
   configuration's `Language`. A missing or unregistered language throws
   `FailedPreconditionException`; a malformed expression throws `InvalidArgumentException`.
6. Adds the `ProcessRegistration`; a duplicate name throws `AlreadyExistsException`.

```csharp
ValueTask RegisterAsync<TProcess>(string? engine = null,
    Action<ProcessConfiguration>? configure = null, CancellationToken ct = default)
    where TProcess : ProcessDefinition;
ValueTask RegisterAsync(ProcessConfiguration configuration, CancellationToken ct = default);
ValueTask UnregisterAsync(string processName, CancellationToken ct = default);
IReadOnlyCollection<string> GetRegisteredProcesses();
bool IsRegistered(string processName);
ProcessRegistration? GetRegistration(string processName);
```

## Runtime capabilities

Every engine declares what it can execute through `IFlowRuntime.Capabilities`, a
`FlowRuntimeCapabilities` flag set:

| Flag                      | Covers                                                                                          |
| ------------------------- | ----------------------------------------------------------------------------------------------- |
| `ProcedureTasks`          | `ProcedureTaskBase` execution (DSL `OnEnter` / `OnLeave` bodies)                                |
| `MultiToken`              | Parallel / inclusive / complex gateways and parallel event-based forks                          |
| `NestedEvents`            | Message / signal catches nested below the root scope (including boundary events on nested hosts) |
| `NestedTimers`            | Timer catches nested below the root scope                                                       |
| `Compensation`            | Compensation boundary events and throw events                                                   |
| `SubProcesses`            | `SubProcess` and `CallActivity`                                                                 |
| `Loops`                   | Standard and multi-instance loop characteristics                                                |
| `NonInterruptingBoundaries` | Non-interrupting boundary events                                                              |

The default `StateMachineEngine` declares `ProcedureTasks` only; `BpmnEngine` declares `All`.
Registration validates the definition against the selected engine's flags and the bridges activated in
the host, so an unsupported shape fails at startup with an exact message instead of degrading silently
at runtime.

`ProcedureTaskBase` executes on both engines with identical semantics: the engine builds a
`FlowTaskContext` (definition, process, token, execution context, payload), awaits `InvokeAsync`, then
resolves the outgoing auto-flow. When no auto-flow resolves, the token parks at the procedure name.
`StateMachineEngine` is the reference implementation; `BpmnEngine` mirrors it point for point.

## FlowRunner

`FlowRunner` implements `IFlowRunner` and is scoped. Resource handlers call it after the resource
request advisors run and after resolving the addressed `SchemataProcess` or token row.

### IFlowRunner

```csharp
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;

public interface IFlowRunner
{
    ValueTask<SchemataProcess> StartAsync<TState>(
        string definitionName, TState source,
        StartProcessOptions? options = null, CancellationToken ct = default)
        where TState : class, ICanonicalName;

    ValueTask<SchemataProcess> StartAsync(
        string definitionName,
        StartProcessOptions? options = null, CancellationToken ct = default);
}
```

### FlowRunner

`FlowRunner` exposes five `StartAsync` overloads in addition to the two `IFlowRunner` ones. The
extra overloads carry a `ClaimsPrincipal?` and are used by the HTTP and gRPC transport handlers so
the foundation layer never reads `HttpContext` directly.

```csharp
ValueTask<SchemataProcess> StartAsync(
    string definitionName, string? source,
    StartProcessOptions? options, ClaimsPrincipal? principal, CancellationToken ct);

ValueTask<SchemataProcess> StartAsync(
    string definitionName,
    StartProcessOptions? options, ClaimsPrincipal? principal, CancellationToken ct);

ValueTask<SchemataProcess> StartAsync<TState>(
    string definitionName, TState source,
    StartProcessOptions? options, ClaimsPrincipal? principal, CancellationToken ct)
    where TState : class, ICanonicalName;

ValueTask<ProcessSnapshot> CompleteAsync(
    SchemataProcess process, string? token,
    ClaimsPrincipal? principal, CancellationToken ct);

ValueTask<ProcessSnapshot> CorrelateAsync(
    SchemataProcess process, string messageName, string? payload,
    string? token, ClaimsPrincipal? principal, CancellationToken ct);

ValueTask<ProcessSnapshot> CorrelateAsync(
    SchemataProcess process, string messageName, object? payload,
    string? token, ClaimsPrincipal? principal, CancellationToken ct);

ValueTask ThrowSignalAsync(
    string signalName, string? payload,
    string? token, ClaimsPrincipal? principal, CancellationToken ct);

ValueTask ThrowSignalAsync(
    string signalName, object? payload,
    string? token, ClaimsPrincipal? principal, CancellationToken ct);

ValueTask<ProcessSnapshot> TerminateAsync(
    SchemataProcess process, ClaimsPrincipal? principal, CancellationToken ct);

ValueTask<ProcessSnapshot> CancelTokenAsync(
    SchemataProcessToken token, ClaimsPrincipal? principal, CancellationToken ct);
```

| Method             | Behavior                                                                                                                                                                                |
| ------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `StartAsync`       | Creates a `SchemataProcess`, optionally binds a source entity via `BindStartSourceAsync`, calls `engine.StartAsync`, runs advisors, persists the snapshot, and returns the process row. |
| `CompleteAsync`    | Loads tokens, calls `engine.AdvanceAsync`, runs advisors, persists, and returns the snapshot.                                                                                           |
| `CorrelateAsync`   | Resolves the `Message` definition by name, picks the target token (the supplied `token` argument, or `engine.FindTriggerTargetsAsync` when omitted), calls `engine.TriggerAsync` for that token, runs advisors, persists, and returns the snapshot.     |
| `ThrowSignalAsync` | Lists every persisted process that has a waiting token, matches each against the signal definition, and calls `engine.TriggerAsync` for every accepted target.                          |
| `TerminateAsync`   | Marks every live token `Cancelled`, sets the process state to `Terminated`, runs advisors, persists, and notifies observers.                                                            |
| `CancelTokenAsync` | Cancels one token, sets the process state to `Cancelled` only when every token is terminal, runs advisors, and persists.                                                                |

`StartProcessOptions` carries `DisplayName`, `Description`, and `IdempotencyKey`. The display fields
are copied onto the process row at start; the idempotency key is stored on the row and guarded by the
unique `(DefinitionName, IdempotencyKey)` index described below. The `StartAsync<TState>` overload
binds the source row to the process through
`SchemataProcessSource`, so source advisors can find and update it during later transitions.

### Per-transition core

Every state-changing method follows the same pattern:

1. Load persisted tokens for the addressed process.
2. Snapshot previous waiting positions for advisor comparisons (`WaitingMap(tokens)`).
3. Create `FlowExecutionContext` with the shared `IUnitOfWork` and application `IServiceProvider`.
4. Invoke the engine driver to compute a `ProcessSnapshot`.
5. For each distinct token in the snapshot, build a `FlowTransitionContext` and run the
   `IFlowSourceAdvisor<TSource>` pipeline for every binding row in that token's scope, then flush
   the entities task code touched through `FlowTaskContext.SourceAsync` / `BindSourceAsync`. The
   projection and write-back semantics are described in the source advisors section below.
6. For each transition in the snapshot, build a `FlowTransitionContext` and run the
   `IFlowTransitionAdvisor` pipeline against it. This runs inside the transition unit of work,
   before the commit: an advisor that throws aborts the transition before anything is persisted.
7. Persist the snapshot through `ProcessPersistence.PersistSnapshotAsync`. `PersistSnapshotAsync`
   upserts the process and token rows and appends transition rows; source-binding rows are written
   separately via `BindStartSourceAsync` (start path) and via the source advisor (transition path).
   All four repositories are joined to one unit of work inside `ProcessPersistence.ExecuteAsync`, so
   the writes commit or roll back together.

After persistence commits, `ProcessLifecycleNotifier` publishes the lifecycle notifications
described below. A failing engine call publishes `NotifyFailedAsync` and rethrows.

`UpdatedBy` is resolved from the `ClaimsPrincipal`: the subject claim becomes `users/{sub}`, falling
back to `Identity.Name`.

## SchemataProcess entity

```csharp
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

[Table("SchemataProcesses")]
[CanonicalName("processes/{process}")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(DefinitionName), nameof(IdempotencyKey), IsUnique = true)]
public class SchemataProcess : IIdentifier, ICanonicalName, IConcurrency, IDescriptive,
                                ISoftDelete, ITimestamp, IStateful, IAnnotatable
{
    public virtual string  DefinitionName { get; set; }  // registered definition name
    public virtual string? IdempotencyKey { get; set; }  // start idempotency key; released on terminal state
    public virtual string? DisplayName    { get; set; }  // optional human-readable label
    public virtual string? Description    { get; set; }  // optional description
    public virtual string? State          { get; set; }  // Running / Waiting / Completed / Failed / Terminated / Cancelled
    public virtual Dictionary<string, string?> Annotations { get; set; }  // client-owned (AIP-148)
}
```

It implements `ISoftDelete`, so the default query filter hides tombstoned instances; read them
inside a `using (repository.SuppressQuerySoftDelete())` scope. `IConcurrency` carries the optimistic
`Timestamp`. The aggregate state on `SchemataProcess.State` is computed by
`TokenAggregator.ApplyAndAggregate`; per-token state lives on `SchemataProcessToken.StateName`,
`SchemataProcessToken.WaitingAtName`, and `SchemataProcessToken.State`. Start idempotency is enforced
by the unique `(DefinitionName, IdempotencyKey)` index: a duplicate live key surfaces as
`AlreadyExistsException`, and when the process reaches a terminal state the key moves into
`Annotations["schemata/flow/idempotency-key"]` so the same key can start again.

## SchemataProcessToken entity

```csharp
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

[Table("SchemataProcessTokens")]
[CanonicalName("processes/{process}/tokens/{token}")]
[PrimaryKey(nameof(Uid))]
public class SchemataProcessToken : IIdentifier, ICanonicalName, IConcurrency, IStateful,
                                     ITimestamp, ISoftDelete, IAnnotatable
{
    public virtual string  Process     { get; set; }  // owning process leaf id
    public virtual string? Spawner     { get; set; }  // canonical name of the spawning token
    public virtual string  ScopeName     { get; set; }  // scope key: process instance name at root, sub-process element name below
    public virtual string  StateName     { get; set; }  // current element name
    public virtual string? WaitingAtName { get; set; }  // waiting element name
    public virtual Dictionary<string, int>    Bookkeeping { get; set; }  // engine-private counters
    public virtual Dictionary<string, string?> Annotations { get; set; }  // client-owned (AIP-148)
    public virtual string? State       { get; set; }  // Active / Waiting / Completed / Failed / Cancelled / Compensating / Compensated
}
```

`Bookkeeping` is engine-owned and persisted as a JSON column through the provider dictionary
conversion; `Annotations` is client-owned and engines never write it. Application payloads and
source rows are represented through `SchemataProcessSource` bindings and source advisors. The set
of live states (`Active`, `Waiting`) and join-counted states (`Waiting`, `Failed`) live in
`Runtime/TokenStates.cs`.

## SchemataProcessTransition entity

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Models;

[Table("SchemataProcessTransitions")]
[CanonicalName("processes/{process}/transitions/{transition}")]
[PrimaryKey(nameof(Uid))]
public class SchemataProcessTransition : IIdentifier, ICanonicalName, IConcurrency,
                                          ITransition, ITimestamp
{
    public virtual string? Process   { get; set; }  // owning process leaf id
    public virtual string? Token     { get; set; }  // token canonical name (one row per state change)
    public virtual TransitionKind Kind { get; set; }  // Move / Cancel / Fail / Fork / Join / Spawn / Compensate
    public virtual string? Previous  { get; set; }  // state before
    public virtual string? Posterior { get; set; }  // state after
    // ITransition: Event (trigger name), Note, UpdatedBy
}
```

One transition row is written per state change, in the same unit of work as the instance update.
The state-machine engine emits `Move`, `Cancel`, and `Fail`; the BPMN engine additionally emits
`Fork`, `Join`, `Spawn`, and `Compensate`.

## Persistence

`ProcessPersistence.ExecuteAsync` opens a unit of work over `IRepository<SchemataProcess>`, joins
the token, transition, source-binding, and compensation repositories into the same unit of work, executes the
supplied runtime work, and commits. `PersistSnapshotAsync` upserts the process and token rows and
appends transition rows. Source-binding rows are written through `BindStartSourceAsync` on the
start path and through `FlowTaskContext.BindSourceAsync` for token-scoped rows; their concurrency
stamps are refreshed by the source advisor pipeline on every transition. All five repositories commit or
roll back together because they share the same unit of work. There is no persistence
observer; durability is built into the runtime's commit path, and lifecycle observers are
post-commit notifications.

### Persisted compensation bindings

Compensation registrations are data, not engine memory. `ProcessSnapshot.CompensationBindings` carries
the scope owner, activity name, and registration order of every armed compensation; each persist
replaces the process's rows in the `SchemataProcessCompensations` table inside the same unit of work
(terminal processes and empty binding lists clear the rows). When the runner loads a process, the
persisted bindings come back through `FlowExecutionContext.LoadedCompensationBindings`, so a throw
after a host restart still resolves its handlers. A missing binding or handler is an explicit
`InvalidOperationException` from the throw path, never a silent no-op.

## Source advisors

`SchemataProcessSource` rows bind a process, or an individual token, to an application source
entity. A row with `Token = null` is a process-level binding; a row naming a token scopes the
binding to that branch. The `(Process, Token, Name)` index is unique, so a binding name identifies
one declaration within its scope. `UseFlow()` registers the default source advisor
`AdviceSourceProjection<TSource>` as a scoped `IFlowSourceAdvisor<TSource>` through
`TryAddEnumerable` in `SchemataFlowFeature`; it runs with `Order = 0`, and additional
`IFlowSourceAdvisor<TSource>` implementations stack alongside it. `FlowRunner` drives the pipeline
once per distinct token per snapshot: projection is a function of state, not of individual
transition events.

### Declaring source bindings

Declare bindings in the `ProcessDefinition` constructor with `BindSource<T>`:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Models;

public sealed class Order : ICanonicalName, IStateful
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
    public string? State         { get; set; }
    public string? PaymentState  { get; set; }
    public string? PaymentPhase  { get; set; }
}

public sealed class OrderProcess : ProcessDefinition
{
    public OrderProcess() {
        BindSource<Order>();                 // convention-named "order"; State slot -> IStateful.State
        BindSource<Order>("payment", b => b
            .State(o => o.PaymentState)
            .Lifecycle(o => o.PaymentPhase)
            .Projection(FlowSourceProjection.Auto));
    }
}
```

The binding name defaults to the type name, underscored and lowered (`Order` becomes `order`).
Overloads cover the convention form, a bare state member selector (`BindSource<Order>(o =>
o.State)`), and the builder form above. A `When<T>` guard condition also registers a default
declaration under the same convention name, with no member configuration; an explicit `BindSource`
declaration with that name replaces the default. One entity type can carry several named bindings:
on a multi-token engine, task code binds each branch to its own row with
`FlowTaskContext.BindSourceAsync(name, entity)`, and each row follows its own token's scope.

Registration compiles every declaration into a `FlowSourceDescriptor` with cached member accessors
(no runtime reflection during projection) and rejects invalid declarations: a duplicated binding
name raises `PROCESS_SOURCE_BINDING_DUPLICATE`, two declarations that target the same member of
one entity type raise `PROCESS_SOURCE_MEMBER_CONFLICT`, and a selector that is not a writable
instance `string?` property raises `PROCESS_SOURCE_MEMBER_INVALID`. Calling `BindSourceAsync` with
an undeclared name is legal and produces a data-only binding row: it takes part in write-back and
the stamp protocol but receives no projection.

### Two slots per binding

| Slot          | Receives                                                                                          | Default member                                                                     |
| ------------- | ------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| **State**     | The business position, per the projection matrix below                                             | `IStateful.State` when the entity implements it; otherwise the `.State(...)` member |
| **Lifecycle** | The binding scope's lifecycle: `Process.State` on process-level rows, the token status on token-level rows | Disabled until `.Lifecycle(...)` names a member                              |

### Projection modes

`FlowSourceProjection` controls what the State slot receives. The Lifecycle slot is independent:
on any mode other than `None`, a declared lifecycle member mirrors the scope lifecycle on every
projection pass.

| Mode            | State slot behavior                                                                                                                                          |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Auto` (default) | The business node name while the binding's scope is live; on a process-level binding, the process lifecycle state (`Completed`, `Failed`, `Terminated`, `Cancelled`) once the process is terminal. |
| `BusinessState` | The business node name while the scope is live. Terminal lifecycle is never written, so the last business value survives process completion.                    |
| `Terminal`      | The business node name while the scope is live (identical to `BusinessState`); on a process-level binding, the terminal process state once the process terminates. |
| `Lifecycle`     | The scope lifecycle on every pass (`Process.State` or the token status), in place of a node name.                                                            |
| `None`          | Nothing, on either slot. Write-back and the stamp protocol still run.                                                                                         |

"Live" has a precise meaning per scope. A token-level row projects while its token's status is
`Active` or `Waiting`; a terminal token stops projecting, which freezes a completed branch's value
(a later join does not overwrite a final business value such as `Captured`, and cancel/fail
visibility belongs to the Lifecycle slot). A process-level row projects while the process is
non-terminal and exactly one token is live. With more than one live token the binding cannot pick
a single business state, so it does not project and the advisor logs one warning per process and
binding recommending token-level bindings or an explicit mode.

Only business activity nodes project. The advisor classifies the token's current element by AST
type (`Activity` and not `ProcedureTaskBase`), never by name, so the synthetic names the DSL
generates (`Await_*` event-based gateways, `End_*` / `Enter_*` / `Catch_*` events, `Decision_*`
gateways) never reach the source row; the State slot keeps its previous value through those hops.
`SubProcess` and `CallActivity` are activities and project their own name while a token rests on
the host node. Writes are idempotent: the advisor compares the projected value against the current
member and skips the update when nothing changed.

### Concurrency-stamp protocol

Each binding row's `SourceTimestamp` mirrors the bound entity's `IConcurrency.Timestamp`. On every
transition the framework validates all binding rows the process holds for that entity, not just
the rows in the current token's scope, and after the entity write it refreshes every one of them
to the new timestamp. Validating and refreshing across the whole entity is what keeps per-token
bindings of one entity from raising false conflicts when a sibling branch writes. A stale row
aborts the transition with `FailedPreconditionException`, reason
`FLOW_SOURCE_MODIFIED_CONCURRENTLY`, which the transports surface as HTTP 412.

Sharing one source entity across two processes trips the same check: after one process writes the
entity, the other process's rows are stale, and its next transition fails with 412. That outcome
is intended; bind per-process entities or coordinate access in application code.

### Write-back

Entities a task loads or binds through `FlowTaskContext.SourceAsync<T>()` or
`BindSourceAsync<T>()` are tracked by identity `(type, canonical name)` and persisted
automatically inside the transition's unit of work. Each tracked entity is written once, so the
task's field mutations and the projection land in a single update, and the behavior matches across
the EF Core and LinqToDB providers. Because the projection pass reuses the tracked instance, it
observes the task's mutations within the same unit of work. Condition evaluation does not track or
write back sources: conditions are pure reads, and application code must not mutate a source
inside a `When<T>` predicate.

Projected values are part of the application's data contract. Consumers read business node names
and lifecycle states from the source row, so renaming a flow node changes what those consumers
observe. Treat node names as stable identifiers once a process runs in production.


## IFlowTransitionAdvisor

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Models;

public interface IFlowTransitionAdvisor : IAdvisor<FlowTransitionContext>
{
}

public class FlowTransitionContext
{
    public ProcessDefinition? Definition          { get; set; }
    public ProcessSnapshot    Snapshot            { get; set; } = null!;
    public required TokenSnapshot Token           { get; init; }
    public string?            PreviousWaitingAtName { get; set; }
    public IUnitOfWork?       UnitOfWork          { get; set; }
}
```

A transition advisor is an `IAdvisor<FlowTransitionContext>`: its `AdviseAsync` returns an
`AdviseResult`, and it runs inside the transition unit of work, before the commit. The built-in
advisors provision wake-up infrastructure for the new waiting state and return `AdviseResult.Continue`; a throwing advisor
aborts the transition so a process never persists into a state whose timer job or event
subscription was never created. The `PreviousWaitingAtName` field is the only source for the
pre-transition waiting element, because the snapshot already reflects the engine result by the time
the advisor runs.

`Schemata.Flow.Event` registers `AdviceTransitionEvent` to maintain
`IRepository<SchemataEventSubscription>` rows; `Schemata.Flow.Scheduling` registers
`AdviceTransitionTimer` to schedule and cancel timer jobs. Both register through
`TryAddEnumerable`, so additional advisors stack alongside. Subscriptions are token-scoped: arming
walks the full element tree, so nested message catches and boundary events on nested hosts each get a
row whose `Token` column names the armed token, while signals keep a process-wide row
(`Token = null`) and broadcast. Correlation therefore routes a bus event to exactly the waiting
token, and two tokens parked on the same message name correlate independently.

## IProcessLifecycleObserver

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;

public interface IProcessLifecycleObserver
{
    Task OnStartedAsync(SchemataProcess process, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnTransitionedAsync(
        SchemataProcess process, SchemataProcessTransition transition,
        CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnTerminatedAsync(SchemataProcess process, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnFailedAsync(SchemataProcess process, Exception exception, CancellationToken ct = default)
        => Task.CompletedTask;
}
```

Each method provides a no-op default body so implementations override only the hooks they care
about. The runtime fires the observers after `ProcessPersistence` commits. Each observer call is
wrapped in its own try/catch; thrown observers are logged at warning and never affect the committed
transition. `Schemata.Flow.Event` registers `ProcessEventLifecycleObserver`, which publishes the
process events (`ProcessStartedEvent`, `TransitionMadeEvent`, `ProcessCompletedEvent`,
`ProcessFailedEvent`) on the event bus. It is the only lifecycle observer interface — the per-token
observer path and its fork/join/cancel events were removed, so lifecycle reactions belong here or in
a transition advisor.

## Extension points

- Implement `IFlowTransitionAdvisor` and register via `TryAddEnumerable` to provision infrastructure
  or veto a transition before it commits.
- Implement `IProcessLifecycleObserver` and register via `TryAddEnumerable` to react after the
  commit.
- Implement `IFlowSourceAdvisor<TSource>` and register it via `TryAddEnumerable` to project runtime
  state onto bound source entities in the transition unit of work.
- Call `IProcessRegistry.RegisterAsync` to add a definition after startup.

## Design rationale

`FlowRunner` takes a `ClaimsPrincipal?` rather than reading `HttpContext`, so the foundation layer
stays free of the transport. The HTTP and gRPC surfaces project their own request into a principal
before calling in.

## Caveats

- `ProcessRegistry` is materialized synchronously during singleton construction, which runs each
  `ProcessDefinition` constructor. Keep those constructors cheap.
- `ThrowSignalAsync` enumerates persisted instances with `WaitingAtName != null` to find signal
  matches. For large deployments, index `WaitingAtName`.
- Source advisors only run when the source type is resolvable, implements `ICanonicalName`, has an
  `IRepository<TSource>` registration, and has a matching `SchemataProcessSource` binding.

## See also

- [Engine](engine.md)
- [BPMN Engine](bpmn-engine.md)
- [HTTP Transport](http.md)
- [Event Integration](event.md)
- [Scheduling Integration](scheduling.md)
- [Expressions](../expressions/overview.md)
