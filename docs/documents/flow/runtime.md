# Flow Runtime Services

`ProcessRegistry` and `ProcessRuntime` bridge the BPMN engine to the rest of the framework.
`ProcessRegistry` holds the compiled definitions and resolves the engine for each. `ProcessRuntime`
is the public entry point: it loads instances, drives the engine, runs the pre-commit advisor
pipeline, writes the source entity when configured, persists the result, and notifies observers. Both
are singletons registered by `SchemataFlowFeature`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Foundation` | `ProcessRegistry.cs`, `ProcessRuntime.cs`, `ProcessRuntime.Events.cs`, `ProcessRuntime.Lifecycle.cs`, `ProcessPersistence.cs`, `ProcessWriteback.cs`, `ProcessInitializer.cs`, `Runtime/ExpressionConditionExpression.cs` |
| `Schemata.Flow.Skeleton` | `Runtime/IProcessRuntime.cs`, `Runtime/IProcessRegistry.cs`, `Runtime/IFlowWritebackProjector.cs`, `Entities/SchemataProcess.cs`, `Entities/SchemataProcessTransition.cs`, `Observers/IFlowTransitionAdvisor.cs`, `Runtime/IProcessLifecycleObserver.cs`, `SchemataFlowOptions.cs` |

## ProcessRegistry

`ProcessRegistry` implements `IProcessRegistry` and is a **singleton**. It stores registrations in a
`ConcurrentDictionary<string, ProcessRegistration>` keyed case-insensitively by process name.

Registering a configuration:

1. Instantiates the `ProcessDefinition` subclass, running its DSL constructor.
2. Calls `Validate` on every `IFlowEngineValidator` whose `EngineName` matches the configuration's
   engine.
3. Resolves the keyed `IFlowRuntime` for that engine; a missing engine throws `NotSupportedException`.
4. Adds the `ProcessRegistration`; a duplicate name throws `AlreadyExistsException`.

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

## ProcessRuntime

`ProcessRuntime` implements `IProcessRuntime` and is a **singleton**. It keeps live instances in an
in-process cache keyed by canonical name, opens its own DI scope per call, and delegates persistence
to `ProcessPersistence`.

```csharp
ValueTask<SchemataProcess>  StartProcessInstanceAsync(string processName,
    IReadOnlyDictionary<string, object?>? variables = null, ClaimsPrincipal? principal = null,
    string? displayName = null, string? description = null, object? sourceEntity = null,
    CancellationToken ct = default);

ValueTask<ProcessInstance>  CompleteActivityAsync(string instanceName,
    IReadOnlyDictionary<string, object?>? variables = null, ClaimsPrincipal? principal = null,
    CancellationToken ct = default);

ValueTask<ProcessInstance>  CorrelateMessageAsync(string instanceName, string messageName,
    object? payload = null, ClaimsPrincipal? principal = null, CancellationToken ct = default);

ValueTask                   ThrowSignalAsync(string signalName,
    object? payload = null, ClaimsPrincipal? principal = null, CancellationToken ct = default);

ValueTask<ProcessInstance>  TriggerEventAsync(string instanceName, IEventDefinition trigger,
    object? payload = null, ClaimsPrincipal? principal = null, CancellationToken ct = default);

ValueTask<ProcessInstance>  TerminateProcessInstanceAsync(string instanceName,
    ClaimsPrincipal? principal = null, CancellationToken ct = default);
```

| Method | What it does |
| --- | --- |
| `StartProcessInstanceAsync` | Creates a `SchemataProcess` for the named definition, calls `engine.StartAsync`, persists, returns the instance row. |
| `CompleteActivityAsync` | Merges variables, calls `engine.AdvanceAsync`, persists. |
| `CorrelateMessageAsync` | Resolves the `Message` definition by name, calls `engine.TriggerAsync`, persists. |
| `ThrowSignalAsync` | Finds every waiting instance matching the signal (cached plus persisted), triggers each. |
| `TriggerEventAsync` | Calls `engine.TriggerAsync` with an explicit `IEventDefinition` (the timer bridge uses this). |
| `TerminateProcessInstanceAsync` | Drives a synthetic terminal result (`State = "Terminated"`, `IsComplete = true`), persists. |

`StartProcessInstanceAsync` accepts `displayName`, `description`, and `sourceEntity` that the
`IProcessRuntime` interface exposes but the transport requests do not necessarily carry;
`sourceEntity` stamps an `ISourceReference` onto the instance so a process can be traced back to the
domain row that started it.

### Per-transition core: ApplyAsync

Every state-changing method routes through a shared `ApplyAsync` step:

1. Snapshot the previous `State`, `WaitingAtId`, and `WaitingAt` from the cached `SchemataProcess`.
2. Invoke the engine driver to compute the new `ProcessInstance`.
3. Copy the engine result onto a clone of the process and build a `SchemataProcessTransition`
   (`Previous`, `Posterior`, `Event`, `UpdatedBy`).
4. Build a `FlowTransitionContext` and run the **`IFlowTransitionAdvisor` pipeline** against it.
   This runs in the pre-commit window: an advisor that throws aborts the transition before anything
   is persisted.
5. Build the optional source writeback callback for the registered engine.
6. Persist the instance row, transition row, and source writeback in one unit of work
   (`ProcessPersistence`).
7. Sync the persisted fields back onto the caller's process and hand back the instance and transition.

After `ApplyAsync` returns, the calling method publishes the relevant lifecycle notification (see
below). A failing engine call publishes failure notifications and rethrows.

`UpdatedBy` is resolved from the `ClaimsPrincipal`: the subject claim becomes `users/{sub}`, falling
back to `Identity.Name`.

## SchemataProcess entity

```csharp
[Table("SchemataProcesses")]
[CanonicalName("processes/{process}")]
[PrimaryKey(nameof(Uid))]
public class SchemataProcess : IIdentifier, ICanonicalName, IConcurrency, IDescriptive,
                               ISourceReference, ISoftDelete, ITimestamp, IStateful
{
    public string  DefinitionName { get; set; }  // registered definition name
    public string? Variables      { get; set; }  // JSON variables
    public string? StateId        { get; set; }  // current element id
    public string? State          { get; set; }  // current element name (IStateful)
    public string? WaitingAtId    { get; set; }  // element id the instance waits at
    public string? WaitingAt      { get; set; }  // element name the instance waits at
    // ISourceReference: SourceType, Source, SourceTimestamp
}
```

It implements `ISoftDelete`, so the default query filter hides tombstoned instances; read them
inside a `using (repository.SuppressQuerySoftDelete())` scope. `IConcurrency` carries the optimistic
`Timestamp`. `SourceTimestamp` records the source entity timestamp captured at start time and after
writeback; later transitions compare it with the current source row when the source implements
`IConcurrency`.

## SchemataProcessTransition entity

```csharp
[Table("SchemataProcessTransitions")]
[CanonicalName("processes/{process}/transitions/{transition}")]
[PrimaryKey(nameof(Uid))]
public class SchemataProcessTransition : IIdentifier, ICanonicalName, IConcurrency,
                                         ITransition, ITimestamp
{
    public string? Process   { get; set; }  // canonical name of the instance
    public string? Previous  { get; set; }  // state before
    public string? Posterior { get; set; }  // state after
    // ITransition: Event (trigger name), Note, UpdatedBy
}
```

One transition row is written per state change, in the same unit of work as the instance update.

## Persistence

`ProcessPersistence` owns the database. `PersistTransitionAsync` opens a unit of work over
`IRepository<SchemataProcess>`, joins `IRepository<SchemataProcessTransition>`, runs the optional
writeback callback, upserts the instance row, adds the transition row, and commits; a failure rolls
back. There is no persistence observer — durability is built into the runtime's commit path, and
`IProcessLifecycleObserver` is purely a post-commit notification surface.

`ProcessInitializer` (a hosted service) hydrates every persisted instance with `WaitingAtId != null`
into the runtime cache at startup, so waiting instances are addressable after a restart.

## Source writeback

`SchemataFlowOptions.SourceWriteback` controls whether transitions project runtime state back to the
source business entity. The default is `true`; set it to `false` to persist only
`SchemataProcess` and `SchemataProcessTransition` rows.

`ProcessWriteback.Build` returns a unit-of-work callback when the process has both `Source` and
`SourceType`, writeback is enabled, and the resolved source type implements `ICanonicalName`. The
callback resolves `IRepository<TSource>`, joins the transition unit of work, and loads the source row
by `CanonicalName == process.Source`.

The default projection is the state-machine projection: when the source entity implements
`IStateful`, it receives `ProcessInstance.State`. That writes the current BPMN display state onto the
business row. Engines with a different runtime position model can register a keyed
`IFlowWritebackProjector` under their `EngineName`; `ProcessWriteback` uses that projector instead of
the default `IStateful` assignment.

Optimistic concurrency uses the source timestamp captured on the process row. When
`process.SourceTimestamp` has a value and the loaded source entity implements `IConcurrency`,
`ProcessWriteback` compares it with `entity.Timestamp`. A mismatch throws
`FailedPreconditionException` and aborts the transition before the process row or transition row
commit. After a successful source update, the refreshed source `Timestamp` is copied back to
`SchemataProcess.SourceTimestamp`, so the next transition checks against the new version.

The writeback runs inside the same unit of work as the instance and transition rows. A writeback
failure rolls back the whole transition; a successful transition keeps the business row, process row,
and transition history together.

## Expression languages

Flow condition expressions route through the shared [Expressions](../expressions/overview.md)
language profile. `SchemataFlowOptions.Expressions` is an `ExpressionLanguageProfile`: enabled
languages are stored in priority order, and the first enabled language is the module default.

`ExpressionConditionExpression` resolves the language on each evaluation call. It reads the active
`SchemataFlowOptions`, lets a process configuration language override select a default, then calls
`ExpressionLanguageResolver.Resolve` against the module profile. The resolved language name selects
the keyed `IExpressionCompiler`; the compiler parses the condition source and compiles it as a
`Func<IReadOnlyDictionary<string, object?>, bool>`.

`SchemataFlowBuilder` implements `IExpressionLanguageBuilder`, so language packages can configure the
Flow profile through the same seam used by other modules:

```csharp
schema.UseFlow()
      .UseAip()
      .UseCel()
      .Use<OrderProcess>();
```

Flow variables round-trip through JSON and can arrive at evaluation as `JsonElement`. Before calling
the compiled predicate, `ExpressionConditionExpression` unwraps JSON objects into string-keyed maps,
arrays into lists, strings into `string`, numbers into `long` or `double`, booleans into `bool`, and
JSON null/unsupported kinds into `null`.

## IFlowTransitionAdvisor

```csharp
public interface IFlowTransitionAdvisor : IAdvisor<FlowTransitionContext>
{
}

public class FlowTransitionContext
{
    public SchemataProcess    Process             { get; set; }
    public ProcessDefinition? Definition          { get; set; }
    public ProcessInstance    Instance            { get; set; }
    public string?            PreviousState       { get; set; }
    public string?            PreviousWaitingAtId { get; set; }
    public string?            PreviousWaitingAt   { get; set; }
    public IEventDefinition?  Trigger             { get; set; }
}
```

A transition advisor is an `IAdvisor<FlowTransitionContext>`: its `AdviseAsync` returns an
`AdviseResult`, and it runs in the pre-commit window. The built-in advisors provision wake-up
infrastructure for the new waiting state and return `AdviseResult.Continue`; a throwing advisor
aborts the transition so an instance never persists into a state whose timer job or event
subscription was never created. The `Previous*` fields are the only source of the pre-transition
values, because `Process` already reflects the engine result by the time the advisor runs.

`Schemata.Flow.Event` registers `FlowEventTransitionAdvisor` to maintain `IEventSubscriptionStore`
entries; `Schemata.Flow.Scheduling` registers `FlowTimerTransitionAdvisor` to schedule and cancel
timer jobs. Both register through `TryAddEnumerable`, so additional advisors stack alongside.

## IProcessLifecycleObserver

```csharp
public interface IProcessLifecycleObserver
{
    Task OnStartedAsync(SchemataProcess process, CancellationToken ct = default);
    Task OnTransitionedAsync(SchemataProcess process, SchemataProcessTransition transition,
        CancellationToken ct = default);
    Task OnTerminatedAsync(SchemataProcess process, CancellationToken ct = default);
    Task OnFailedAsync(SchemataProcess process, Exception exception, CancellationToken ct = default);
}
```

Lifecycle observers run after the commit. Each is invoked in its own try/catch — a thrown observer
is logged at warning and does not affect the committed transition. `Schemata.Flow.Event` registers
`ProcessEventLifecycleObserver` to publish `ProcessStartedEvent`, `TransitionMadeEvent`,
`ProcessCompletedEvent`, and `ProcessFailedEvent` on `IEventBus` when the event bridge is active.

## Extension points

- Implement `IFlowTransitionAdvisor` and register via `TryAddEnumerable` to provision infrastructure
  or veto a transition before it commits.
- Implement `IProcessLifecycleObserver` and register via `TryAddEnumerable` to react after the commit.
- Implement `IFlowWritebackProjector` and register it as a keyed service under an engine name to
  project runtime state onto source entities differently from the default `IStateful` projection.
- Call `IProcessRegistry.RegisterAsync` to add a definition after startup.

## Design rationale

`ProcessRuntime` takes a `ClaimsPrincipal?` rather than reading `HttpContext`, so the foundation
layer stays free of the transport. The HTTP and gRPC surfaces project their own request into a
principal before calling in.

## Caveats

- `ProcessRegistry` is materialized synchronously during singleton construction, which runs each
  `ProcessDefinition` constructor. Keep those constructors cheap.
- `ThrowSignalAsync` enumerates cached instances and persisted instances with `WaitingAtId != null`
  to find signal matches. For large deployments, index `WaitingAtId`.
- Source writeback only starts when the source type is resolvable in `AppDomainTypeCache`, implements
  `ICanonicalName`, and has an `IRepository<TSource>` registration.

## See also

- [Engine](engine.md)
- [HTTP Transport](http.md)
- [Event Integration](event.md)
- [Scheduling Integration](scheduling.md)
- [Expressions](../expressions/overview.md)
