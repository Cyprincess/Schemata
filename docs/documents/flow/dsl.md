# Flow DSL

The Flow DSL builds a BPMN graph inside a `ProcessDefinition` subclass. Elements are declared as
**magic properties** that the base constructor materializes by reflection; the constructor body then
wires them together with a fluent, step-returning builder. The builder lives in
`Schemata.Flow.Skeleton.Builders` as extension methods on `ProcessDefinition`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Skeleton` | `Builders/ProcessBuilder.cs`, `Builders/StartFlow.cs`, `Builders/ActivityBehavior.cs`, `Builders/Branch.cs`, `Builders/EventBranch.cs`, `Builders/FlowBranch.cs`, `Builders/BoundaryCatch.cs`, `Builders/ParallelFork.cs`, `Builders/ParallelJoin.cs`, `Builders/InclusiveBranch.cs`, `Builders/InclusiveMerge.cs` |

## Magic properties

Declare BPMN elements as get-only auto-properties initialized to `null!`. The base constructor
instantiates each null property and sets its `Name` to the property name.

```csharp
public UserTask Draft  { get; } = null!;
public EndEvent Done   { get; } = null!;
public Message  Pay    { get; } = null!;
```

| Property type | Materialized element | Registered to |
| --- | --- | --- |
| `NoneTask`, `ServiceTask`, `UserTask`, `SendTask`, `ReceiveTask`, `ScriptTask`, `ManualTask`, `BusinessRuleTask`, and other `Activity` subtypes | the activity | `Elements` |
| `StartEvent` | `FlowEvent { Position = Start }` | `Elements` |
| `EndEvent` | `FlowEvent { Position = End }` | `Elements` |
| `FlowEvent` | `FlowEvent` (default position) | `Elements` |
| `Message` | `Message` | `Messages` |
| `Signal` | `Signal` | `Signals` |
| `ErrorDefinition` | `ErrorDefinition` | `Errors` |
| `EscalationDefinition` | `EscalationDefinition` | `Escalations` |

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

The branch and event factories also hang off `ProcessDefinition`:

```csharp
this.When<T>(Func<T, bool> predicate)         // typed guarded branch
this.When(IConditionExpression condition)     // explicit guarded branch
this.Otherwise()                              // default branch
this.On(Message message)                      // event-based gateway branch
this.On(Signal signal)
this.OnTimer(TimeSpan duration)
this.OnCondition(ConditionalDefinition c)
```

## StartFlow

`Start(...)` returns a `StartFlow`. It synthesizes the start `FlowEvent` lazily when you choose the
continuation:

| Method | Effect | Returns |
| --- | --- | --- |
| `.Go(Activity)` | start event flows into the activity | `ActivityBehavior` |
| `.Await(EventBranch[])` | start event flows into an event-based gateway | `ProcessDefinition` |

## ActivityBehavior

`During(activity)` returns the central builder, `ActivityBehavior`. It controls two concerns:

- **Outgoing path** — exactly one per activity. A second path-defining call on the same activity
  throws `InvalidOperationException`.
- **Boundary events** — any number, attached to the activity.

### Outgoing path

| Method | BPMN semantics | Returns |
| --- | --- | --- |
| `.Go(Activity)` / `.Go(FlowEvent)` | unconditional sequence flow | `ActivityBehavior` |
| `.End()` | synthesized anonymous end event | `ActivityBehavior` |
| `.End(EndEvent)` / `.End(FlowEvent)` | route to an existing end event | `ActivityBehavior` |
| `.Terminate()` | synthesized terminate end event (`IsTerminate = true`) | `ActivityBehavior` |
| `.Decide(Branch[])` | exclusive gateway (XOR) | `ActivityBehavior` |
| `.Include(Branch[])` | inclusive gateway (OR) | `InclusiveBranch` |
| `.Fork(FlowBranch[])` | parallel gateway (AND) | `ParallelFork` |
| `.Await(EventBranch[])` | event-based gateway | `ActivityBehavior` |

`.Go(Activity)` chains: it advances an internal tail so `During(A).Go(B).Go(C)` wires A→B→C.

### Boundary events

Each method attaches a boundary `FlowEvent` to the activity and returns a `BoundaryCatch`:

| Method | Definition attached |
| --- | --- |
| `.OnError<TException>()` | `ErrorDefinition` named after the exception type |
| `.OnError(ErrorDefinition)` | the supplied error |
| `.OnTimer(TimeSpan)` | `TimerDefinition` (Duration) |
| `.OnMessage(Message)` | the message |
| `.OnSignal(Signal)` | the signal |
| `.OnCondition(ConditionalDefinition)` | the condition |
| `.OnEscalation(EscalationDefinition)` | the escalation |
| `.OnCompensation()` | `CompensationDefinition` |
| `.OnCancel()` | `CancelDefinition` |

## Branch

`Branch` is the unit of `.Decide()` and `.Include()`, created by `When<T>`, `When`, or `Otherwise`:

```csharp
this.When<Order>(o => o.Amount > 100).Go(Review)
this.Otherwise().Go(Reject)
```

`When<T>(Expression)` derives the variable key from the type name via Humanizer
(`typeof(T).Name.Underscore().ToLowerInvariant()`), so `Order` reads variable `order` and
`OrderRequest` reads `order_request`. The lambda pulls that key from
`FlowConditionContext.Variables`, deserializes the JSON value to `T` with the shared case-insensitive
options, and applies the predicate. A value already of type `T` is used directly.

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

`ParallelJoin.Go(Activity)` continues after the join. These build `ParallelGateway` elements, which
the state machine rejects; they target engines that execute parallel flow.

## InclusiveBranch, InclusiveMerge

`.Include()` returns an `InclusiveBranch` whose `.Merge(exits)` returns an `InclusiveMerge`:

```csharp
this.During(Prepare).Include(
        this.When<Order>(o => o.NeedsPackaging).Go(Pack),
        this.When<Order>(o => o.NeedsInsurance).Go(Insure))
    .Merge(Pack, Insure)
    .Go(Ship);
```

These build `InclusiveGateway` elements, also state-machine-rejected.

## EventBranch

`On()`, `OnTimer()`, and `OnCondition()` create `EventBranch` values for `.Await()`. Each branch
becomes an intermediate catch event hanging off an event-based gateway; the first event to fire wins:

```csharp
this.During(Approve).Await(
    this.On(Pay).Go(Fulfill),
    this.On(Cancel).Go(Cancelled),
    this.OnTimer(TimeSpan.FromMinutes(30)).Go(TimedOut));
```

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

## Complete example

The pattern, taken from the engine tests:

```csharp
public class EventBasedConditionalProcess : ProcessDefinition
{
    public NoneTask New         { get; } = null!;
    public NoneTask Processing  { get; } = null!;
    public NoneTask Rejected    { get; } = null!;
    public EndEvent RejectedEnd { get; } = null!;
    public Message  Pay         { get; } = null!;

    public EventBasedConditionalProcess()
    {
        this.Start().Go(New);

        this.During(New)
            .Await(this.On(Pay)
                       .Decide(
                            this.When<Order>(o => o.Amount > 0).Go(Processing),
                            this.Otherwise().Go(Rejected)));

        this.During(Processing).End();
        this.During(Rejected).End(RejectedEnd);
    }
}
```

## Extension points

- Implement `IConditionExpression` for a custom guard evaluator and pass it to `When(...)`.
- Use `[DisplayName("...")]` on a magic property to control the persisted state label.

## Design rationale

Each builder step returns a distinct type, so the compiler enforces a valid construction order:
`.Join()` is reachable only from `.Fork()`, `.Merge()` only from `.Include()`. Defining a second
outgoing path on an activity throws at construction time rather than producing an ambiguous graph.

## Caveats

- Magic properties must be auto-properties; hand-written backing fields are not discovered.
- `When<T>` keys variables by the lowered, underscored type name. Two variables of the same CLR type
  collide; use `When(IConditionExpression)` with an explicit key lookup to disambiguate.
- `.NonInterrupting()` on `BoundaryCatch` is accepted by the DSL, but the `StateMachineValidator`
  rejects non-interrupting boundary events.

## See also

- [AST Reference](ast.md)
- [Engine](engine.md)
- [Validator](validator.md)
