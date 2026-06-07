# Flow DSL

The Flow DSL is a fluent API for building BPMN process graphs inside a `ProcessDefinition` subclass. It uses **magic properties** declared as auto-properties on the subclass and materialized by the base constructor via reflection, together with a chainable builder API that guides you through valid graph construction step by step.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Skeleton` | `Builders/ProcessBuilder.cs`, `Builders/ActivityBehavior.cs`, `Builders/Branch.cs`, `Builders/EventBranch.cs`, `Builders/FlowBranch.cs`, `Builders/BoundaryCatch.cs`, `Builders/ParallelFork.cs`, `Builders/ParallelJoin.cs`, `Builders/InclusiveBranch.cs`, `Builders/InclusiveMerge.cs`, `Builders/StartFlow.cs` |

## Magic properties

Declare BPMN elements as `{ get; private set; }` properties. The base `ProcessDefinition` constructor instantiates any that are `null` and writes them back into the property.

| Property type | Materialized as | Registered to |
| --- | --- | --- |
| `NoneTask`, `ServiceTask`, `UserTask`, `SendTask`, `ReceiveTask`, `ScriptTask`, `ManualTask`, `BusinessRuleTask` | Activity of that type | `Elements` |
| `EmbeddedSubProcess`, `EventSubProcess`, `CallActivity` | Activity subtype | `Elements` |
| `StartEvent` | `FlowEvent { Position = Start }` | `Elements` |
| `EndEvent` | `FlowEvent { Position = End }` | `Elements` |
| `FlowEvent` | `FlowEvent` (Position left at default) | `Elements` |
| `Message` | `Message` | `Messages` |
| `Signal` | `Signal` | `Signals` |
| `ErrorDefinition` | `ErrorDefinition` | `Errors` |
| `EscalationDefinition` | `EscalationDefinition` | `Escalations` |

`[DisplayName("...")]` on the property overrides `Name` with the attribute value.

## Entry points

The DSL is accessed through extension methods on `ProcessDefinition` in the static class `ProcessBuilder`. Within a `ProcessDefinition` subclass constructor, call these as `this.Start()` or just `Start()`:

```csharp
Start()                                    // None start event
Start(Message message)                     // Message start event
Start(Signal signal)                       // Signal start event
Start(TimerDefinition timer)               // Timer start event
Start(ConditionalDefinition condition)     // Conditional start event
During(Activity activity)                  // ActivityBehavior for an existing activity
```

## StartFlow

Defines the start of the process. Creates a `FlowEvent` with `Position = Start`.

```csharp
StartFlow.Go(Activity)         // -> ActivityBehavior
StartFlow.Await(EventBranch[]) // -> ProcessDefinition
```

## ActivityBehavior

The central DSL type for defining what happens on an `Activity`. Obtained from `During(activity)`, `StartFlow.Go()`, `ParallelJoin.Go()`, `BoundaryCatch.Go()`, etc.

An `ActivityBehavior` controls two independent concerns:

1. **Outgoing path** — where the token goes after the activity completes. At most one may be defined.
2. **Boundary events** — events that fire during execution. Any number may be defined.

### Outgoing path methods

| Method | BPMN semantics | Returns |
| --- | --- | --- |
| `.Go(Activity)` / `.Go(FlowEvent)` | Unconditional sequence flow | `ActivityBehavior` |
| `.End()` | Anonymous end event | `ActivityBehavior` |
| `.End(EndEvent)` | Named end event | `ActivityBehavior` |
| `.Terminate()` | Terminate end event | `ActivityBehavior` |
| `.Decide(Branch[])` | Exclusive decision (XOR) | `ActivityBehavior` |
| `.Include(Branch[])` | Inclusive decision (OR) | `InclusiveBranch` |
| `.Fork(FlowBranch[])` | Parallel fork (AND) | `ParallelFork` |
| `.Await(EventBranch[])` | Event-based decision | `ActivityBehavior` |

### Boundary event methods

| Method | BPMN semantics | Returns |
| --- | --- | --- |
| `.OnError<TException>()` | Error boundary (always interrupting) | `BoundaryCatch` |
| `.OnError(ErrorDefinition)` | Error boundary with definition | `BoundaryCatch` |
| `.OnTimer(TimeSpan)` | Timer boundary | `BoundaryCatch` |
| `.OnMessage(Message)` | Message boundary | `BoundaryCatch` |
| `.OnSignal(Signal)` | Signal boundary | `BoundaryCatch` |
| `.OnCondition(ConditionalDefinition)` | Conditional boundary | `BoundaryCatch` |
| `.OnEscalation(EscalationDefinition)` | Escalation boundary | `BoundaryCatch` |
| `.OnCompensation()` | Compensation boundary | `BoundaryCatch` |
| `.OnCancel()` | Cancel boundary | `BoundaryCatch` |

## Branch and FlowBranch

### Branch (conditional)

Used by `.Decide()` and `.Include()`. Created by `When<T>()`, `When(IConditionExpression)`, or `Otherwise()`:

```csharp
When<Order>(o => o.Amount > 100).Go(Review)
Otherwise().Go(Reject)
```

`When<T>(Expression<Func<T, bool>>)` infers the variable key from the type name converted to `snake_case` via Humanizer, deserializes the JSON value from process variables, and evaluates the compiled expression.

### FlowBranch (unconditional)

Used by `.Fork()`. Implicitly convertible from a single `Activity` or from an `ActivityBehavior` chain:

```csharp
// Single-step branch
During(Prepare).Fork(ChargeCard, PackItems)

// Multi-step branch
During(Prepare).Fork(
    During(PickItems).Go(PackItems).Go(PrintLabel),
    ChargeCard)
```

## ParallelFork and ParallelJoin

`.Fork()` returns `ParallelFork`, which only exposes `.Join()`:

```csharp
During(Prepare).Fork(
        During(PickItems).Go(PackItems).Go(PrintLabel),
        ChargeCard)
    .Join()          // auto-derived from branches: waits for PrintLabel + ChargeCard
    .Go(Ship);
```

## InclusiveBranch and InclusiveMerge

`.Include()` returns `InclusiveBranch`, which optionally allows `.Merge()`:

```csharp
During(Prepare).Include(
        When<Order>(o => o.NeedsPackaging).Go(Pack),
        When<Order>(o => o.NeedsInsurance).Go(Insure))
    .Merge()         // synchronizing inclusive merge
    .Go(Ship);
```

If `.Merge()` is omitted, branches converge via implicit XOR merge (pass-through).

## EventBranch

Created by `On()`, `OnTimer()`, `OnCondition()`. Each `EventBranch` creates an `IntermediateCatch` event that waits for the specified trigger. The first branch whose event fires determines the path:

```csharp
During(Approve).Await(
    On(Pay).Go(Fulfill),
    On(Cancel).Go(Cancelled),
    OnTimer(TimeSpan.FromMinutes(30)).Go(TimedOut));
```

An `EventBranch` can also carry an XOR decision after the catch event:

```csharp
During(New).Await(
    On(Pay).Decide(
        When<Order>(o => o.Amount > 0).Go(Processing),
        Otherwise().Go(Rejected)));
```

## BoundaryCatch

Created by boundary event methods on `ActivityBehavior`. Creates a `FlowEvent` with `Position = Boundary`, attaches it to the activity, and creates the outgoing flow. `.Go()` returns the original `ActivityBehavior` for chaining:

```csharp
During(Approve)
    .OnError<TimeoutException>().Go(TimedOut)
    .Await(
        On(Pay).Go(Fulfill),
        On(Cancel).Go(Cancelled));
```

## Complete example

```csharp
public class ApprovalProcess : ProcessDefinition
{
    public UserTask Draft    { get; private set; } = null!;
    public UserTask Review   { get; private set; } = null!;
    public EndEvent Approved { get; private set; } = null!;
    public EndEvent Rejected { get; private set; } = null!;

    public Message PaymentReceived { get; private set; } = null!;

    public ApprovalProcess() {
        Start().Go(Draft);

        During(Draft).Decide(
            When<Request>(r => r.Approved).Go(Review),
            Otherwise().Go(Rejected));

        During(Review).Await(
            On(PaymentReceived).Go(Approved),
            OnTimer(TimeSpan.FromDays(7)).Go(Rejected));
    }
}
```

## Extension points

- Implement `IConditionExpression` to plug in a custom expression evaluator.
- Use `[DisplayName("...")]` on magic properties to control the state label stored in `SchemataProcess.State`.

## Design motivation

The fluent API returns distinct types at each step (`StartFlow`, `ActivityBehavior`, `ParallelFork`, etc.) so the compiler enforces valid BPMN construction. You cannot call `.Join()` before `.Fork()`, and you cannot add a second outgoing path to an activity that already has one. This moves a class of BPMN modeling errors from runtime to compile time.

## Caveats

- Magic properties must be auto-properties. Hand-written backing fields are not discovered by `InitializeProperties()`.
- `When<T>` uses the type name as the variable key. If two variables share the same CLR type, use `When(IConditionExpression)` with an explicit key lookup instead.
- The `StateMachineValidator` rejects non-interrupting boundary events. `.NonInterrupting()` on `BoundaryCatch` is accepted by the DSL but will fail validation.

## See also

- [AST Reference](ast.md)
- [Engine](engine.md)
- [Validator](validator.md)
