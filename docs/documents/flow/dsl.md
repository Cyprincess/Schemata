# Flow DSL

The Flow DSL builds a BPMN graph inside a `ProcessDefinition` subclass. Elements are declared as
**magic properties** that the base constructor materializes by reflection; the constructor body
then wires them together with a fluent, step-returning builder. The builder lives in
`Schemata.Flow.Skeleton.Builders` as extension methods on `ProcessDefinition`.

## Where the code lives

| Package                  | Key files                                                                                                                                                                                                                                                                                                          |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Schemata.Flow.Skeleton` | `Builders/ProcessBuilder.cs`, `Builders/StartFlow.cs`, `Builders/ActivityBehavior.cs`, `Builders/Branch.cs`, `Builders/EventBranch.cs`, `Builders/FlowBranch.cs`, `Builders/BoundaryCatch.cs`, `Builders/ParallelFork.cs`, `Builders/ParallelJoin.cs`, `Builders/InclusiveBranch.cs`, `Builders/InclusiveMerge.cs` |

## Magic properties

Declare BPMN elements as get-only auto-properties initialized to `null!`. The base constructor
instantiates each null property and sets its `Name` to the property name.

```csharp
public UserTask           Draft  { get; } = null!;
public EndEvent           Done   { get; } = null!;
public Message            Pay    { get; } = null!;
public Message<OrderPaid> PayPaid{ get; } = null!;
```

| Property type                                                                                                                                                | Materialized element             | Registered to |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------- | ------------- |
| `NoneTask`, `ServiceTask`, `UserTask`, `SendTask`, `ReceiveTask`, `ScriptTask`, `ManualTask`, `BusinessRuleTask`, and other non-abstract `Activity` subtypes | the activity                     | `Elements`    |
| `StartEvent`                                                                                                                                                 | `FlowEvent { Position = Start }` | `Elements`    |
| `EndEvent`                                                                                                                                                   | `FlowEvent { Position = End }`   | `Elements`    |
| `FlowEvent`                                                                                                                                                  | `FlowEvent` (default position)   | `Elements`    |
| `Message` and `Message<TPayload>`                                                                                                                            | the message                      | `Messages`    |
| `Signal` and `Signal<TPayload>`                                                                                                                              | the signal                       | `Signals`     |
| `ErrorDefinition`                                                                                                                                            | the error                        | `Errors`      |
| `EscalationDefinition`                                                                                                                                       | the escalation                   | `Escalations` |

`[DisplayName("...")]` on a property overrides `Name`, which becomes the value persisted to
`SchemataProcess.State`.

## Entry points

`ProcessBuilder` adds extension methods on `ProcessDefinition`. Inside the subclass constructor,
call them on `this`:

```csharp
this.Start()                                  // none start event -> StartFlow
this.Start(Message message)                   // message start
this.Start(Signal signal)                     // signal start
this.Start(TimerDefinition timer)             // timer start
this.Start(ConditionalDefinition condition)   // conditional start
this.During(Activity activity)                // -> ActivityBehavior for that activity
```

The branch, event, join, and merge factories also hang off `ProcessDefinition`:

```csharp
this.When<T>(Func<T, bool> predicate)                          // typed guarded branch
this.When<T>(string source, Func<T, bool> predicate)           // explicit binding name
this.When<TSource, TPayload>(Message<TPayload>, Func<TSource, TPayload, bool>)
                                                                 // typed message branch
this.When<TSource, TPayload>(string source, Message<TPayload>, Func<TSource, TPayload, bool>)
                                                                 // explicit binding name
this.When(IConditionExpression condition)                      // explicit guarded branch
this.Otherwise()                                               // default branch
this.On(Message message)                                       // event-based gateway branch
this.On(Signal signal)
this.OnTimer(TimeSpan duration)
this.OnCondition(ConditionalDefinition c)
this.Join(params Activity[] exits)                             // implicit parallel join
this.Merge(params Activity[] exits)                            // implicit inclusive merge
```

`When<T>` and `When<TSource, TPayload>` carry the constraint `T : class, ICanonicalName`; the
binding name comes from `typeof(T).Name.Underscore().ToLowerInvariant()`.

## StartFlow

`Start(...)` returns a `StartFlow`. It synthesizes the start `FlowEvent` lazily when you choose the
continuation:

| Method                         | Effect                                        | Returns             |
| ------------------------------ | --------------------------------------------- | ------------------- |
| `.Go(Activity)`                | start event flows into the activity           | `ActivityBehavior`  |
| `.Await(params EventBranch[])` | start event flows into an event-based gateway | `ProcessDefinition` |

## ActivityBehavior

`During(activity)` returns the central builder, `ActivityBehavior`. It controls two concerns:

- **Outgoing path** — Exactly one per activity. A second path-defining call on the same activity
  throws `InvalidOperationException`.
- **Boundary events** — Any number, attached to the activity.
- **Procedure tasks** — `OnEnter` and `OnLeave` materialize a `ProcedureTask` before or after the
  activity.

### Outgoing path

| Method                               | BPMN semantics                                         | Returns            |
| ------------------------------------ | ------------------------------------------------------ | ------------------ |
| `.Go(Activity)` / `.Go(FlowEvent)`   | unconditional sequence flow                            | `ActivityBehavior` |
| `.End()`                             | synthesized anonymous end event                        | `ActivityBehavior` |
| `.End(EndEvent)` / `.End(FlowEvent)` | route to an existing end event                         | `ActivityBehavior` |
| `.Terminate()`                       | synthesized terminate end event (`IsTerminate = true`) | `ActivityBehavior` |
| `.Decide(params Branch[])`           | exclusive gateway (XOR)                                | `ActivityBehavior` |
| `.Include(params Branch[])`          | inclusive gateway (OR)                                 | `InclusiveBranch`  |
| `.Fork(params FlowBranch[])`         | parallel gateway (AND)                                 | `ParallelFork`     |
| `.Await(params EventBranch[])`       | event-based gateway                                    | `ActivityBehavior` |

`.Go(Activity)` chains: it advances an internal tail so `During(A).Go(B).Go(C)` wires A→B→C.

### Procedure tasks

Procedure tasks are anonymous elements: the task name is synthesized (`Enter_{activity}` /
`Leave_{activity}`).

| Method                                                                             | Effect                                                                                                   |
| ---------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `.OnEnter(Func<FlowTaskContext, ValueTask> body)`                                  | materializes a `ProcedureTask` before the configured activity; every inbound edge routes through it regardless of declaration order |
| `.OnEnter<TSource>(Func<FlowTaskContext, TSource, ValueTask> body)`                | typed variant; resolves the source bound under the name derived from `TSource` and passes it to the body |
| `.OnEnter<TSource>(string source, Func<FlowTaskContext, TSource, ValueTask> body)` | typed variant with an explicit source binding name                                                       |
| `.OnLeave(Func<FlowTaskContext, ValueTask> body)`                                  | materializes a `ProcedureTask` after the activity, then chains it via `.Go(...)`                         |
| `.OnLeave<TSource>(Func<FlowTaskContext, TSource, ValueTask> body)`                | typed variant; resolves the source bound under the name derived from `TSource`                           |
| `.OnLeave<TSource>(string source, Func<FlowTaskContext, TSource, ValueTask> body)` | typed variant with an explicit source binding name                                                       |

### Boundary events

Each method attaches a boundary `FlowEvent` to the activity and returns a `BoundaryCatch`:

| Method                                | Definition attached                              |
| ------------------------------------- | ------------------------------------------------ |
| `.OnError<TException>()`              | `ErrorDefinition` named after the exception type |
| `.OnError(ErrorDefinition)`           | the supplied error                               |
| `.OnTimer(TimeSpan)`                  | `TimerDefinition` (Duration)                     |
| `.OnMessage(Message)`                 | the message                                      |
| `.OnSignal(Signal)`                   | the signal                                       |
| `.OnCondition(ConditionalDefinition)` | the condition                                    |
| `.OnEscalation(EscalationDefinition)` | the escalation                                   |
| `.OnCompensation()`                   | `CompensationDefinition`                         |
| `.OnCancel()`                         | `CancelDefinition`                               |

`.OnEscalation(...)` creates a non-interrupting boundary by default; other boundary catches
default to interrupting unless `.NonInterrupting()` is called on the resulting `BoundaryCatch`.

## Branch

`Branch` is the unit of `.Decide()` and `.Include()`, created by `When<T>`,
`When<TSource, TPayload>`, `When`, or `Otherwise`:

```csharp
this.When<Order>(o => o.Amount > 100).Go(Review)
this.Otherwise().Go(Reject)
```

`When<T>(predicate)` derives the source-binding key from the type name via Humanizer
(`typeof(T).Name.Underscore().ToLowerInvariant()`), so `Order` reads binding `order` and
`OrderRequest` reads `order_request`. The lambda resolves that binding to `T` and applies the
predicate. `When<TSource, TPayload>(Message<TPayload>, predicate)` builds a typed-message branch.

## FlowBranch, ParallelFork, ParallelJoin

`.Fork()` takes `FlowBranch` arguments, implicitly convertible from a single `Activity` or an
`ActivityBehavior` chain, and returns a `ParallelFork` whose only continuation is `.Join()`:

```csharp
this.During(Prepare).Fork(
        this.During(PickItems).Go(PackItems).Go(PrintLabel),
        ChargeCard)
    .Join(PrintLabel, ChargeCard)   // explicit join exits
    .Go(Ship);
```

`ParallelJoin.Go(Activity)` continues after the join. `ProcessBuilder.Join(params Activity[])`
also works as an implicit-join entry point that synthesizes a `ParallelGateway`. These build
`ParallelGateway` elements, which the state machine rejects; they target engines that execute
parallel flow.

## InclusiveBranch, InclusiveMerge

`.Include()` returns an `InclusiveBranch` whose `.Merge(exits)` returns an `InclusiveMerge`:

```csharp
this.During(Prepare).Include(
        this.When<Order>(o => o.NeedsPackaging).Go(Pack),
        this.When<Order>(o => o.NeedsInsurance).Go(Insure))
    .Merge(Pack, Insure)
    .Go(Ship);
```

`ProcessBuilder.Merge(params Activity[])` exposes the same shape as an entry point. These build
`InclusiveGateway` elements, also state-machine-rejected.

## EventBranch

`On(Message)`, `On(Signal)`, `OnTimer()`, and `OnCondition()` create `EventBranch` values for
`.Await()`. Each branch becomes an intermediate catch event hanging off an event-based gateway;
the first event to fire wins:

```csharp
this.During(Approve).Await(
    this.On(Pay).Go(Fulfill),
    this.On(Cancel).Go(Cancelled),
    this.OnTimer(TimeSpan.FromMinutes(30)).Go(TimedOut));
```

Under the state-machine engine, a `NoneTask` whose only outgoing path is `.Await(...)` parks at the
gateway the moment the token arrives, so the branches are correlatable without an explicit
complete; a `UserTask` reaches the gateway when its work item completes.

An `EventBranch` can carry an XOR decision after the catch:

```csharp
this.During(New).Await(
    this.On(Pay).Decide(
        this.When<Order>(o => o.Amount > 0).Go(Processing),
        this.Otherwise().Go(Rejected)));
```

## BoundaryCatch

Boundary methods return a `BoundaryCatch`; its `.Go(target)` returns the original `ActivityBehavior`
so the activity's main path can continue in the same chain:

```csharp
this.During(Processing)
    .OnError<TimeoutException>().Go(Rejected);
this.During(Processing).End();
```

`.NonInterrupting()` on `BoundaryCatch` marks the catch as non-interrupting. The state-machine
engine rejects non-interrupting boundaries, so this is a BPMN-only path.

## Complete example

```csharp
public sealed class OrderApprovalProcess : ProcessDefinition
{
    public NoneTask     Submitted  { get; } = null!;
    public NoneTask     Review     { get; } = null!;
    public EndEvent     Approved   { get; } = null!;
    public EndEvent     Rejected   { get; } = null!;
    public Message      Pay        { get; } = null!;

    public OrderApprovalProcess() {
        this.Start().Go(Submitted);

        this.During(Submitted)
            .Await(this.On(Pay)
                       .Decide(
                            this.When<Order>(o => o.Amount > 0).Go(Review),
                            this.Otherwise().Go(Rejected)));

        this.During(Review).End(Approved);
        this.During(Rejected).End();
    }
}
```

## Extension points

- Implement `IConditionExpression` for a custom guard evaluator and pass it to `When(...)`.
- Use `[DisplayName("...")]` on a magic property to control the persisted state label.

## Design rationale

Each builder step returns a distinct type, so the compiler enforces a valid construction order:
`.Join()` is reachable only from `.Fork()` or `ProcessBuilder.Join(...)`; `.Merge()` only from
`.Include()` or `ProcessBuilder.Merge(...)`. Defining a second outgoing path on an activity
throws `InvalidOperationException` at construction time.

## Caveats

- Magic properties must be auto-properties; hand-written backing fields are not discovered.
- `When<T>` keys source bindings by the lowered, underscored type name. Two bindings of the same
  CLR type collide; use the `When<T>(string source, ...)`, `OnEnter<TSource>(string source, ...)`,
  and `OnLeave<TSource>(string source, ...)` overloads (or
  `FlowTaskContext.SourceAsync<TEntity>(string name, ...)` at the task layer) to disambiguate.
- `.OnEscalation(EscalationDefinition)` creates a non-interrupting boundary by default; other
  boundary catches default to interrupting unless `.NonInterrupting()` is called.
- `.NonInterrupting()` on `BoundaryCatch` is accepted by the DSL, but the `StateMachineValidator`
  rejects non-interrupting boundary events.
- `ProcedureTask<TPayload>` requires an upstream typed message or signal catch with a matching
  payload type; mismatches raise `InvalidOperationException` at registration.
- `OnEnter` routing is declaration-order-independent: edges declared before the call are rerouted,
  edges declared afterwards target the enter task directly. Flows added around the DSL that bypass
  an enter task are rejected at validation (`STATE_MACHINE_ENTER_TASK_BYPASSED`).
- A `NoneTask` whose only outgoing path is `.Await(...)` or `.End()` passes through on arrival
  under the state-machine engine; boundary catches on such a task can never fire and are rejected
  at validation (`STATE_MACHINE_NONE_TASK_BOUNDARY_UNREACHABLE`).

## See also

- [AST Reference](ast.md)
- [Engine](engine.md)
- [Validator](validator.md)
- [State Machine](state-machine.md)
