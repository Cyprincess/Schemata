# Flow DSL

> **Source:** `Schemata.Flow.Skeleton/Builders/`

The Flow DSL is a fluent API for building BPMN process graphs inside a `ProcessDefinition` subclass. It uses **magic properties** — declared as auto-properties on the subclass and materialized by the base constructor via reflection — together with a chainable builder API that guides you through valid graph construction step by step.

## Magic Properties

Declare BPMN elements as `{ get; private set; }` properties. The base `ProcessDefinition` constructor instantiates any that are `null` and writes them back into the property.

### Declarable Types

| Property Type | Materialized As | Registered To |
|---|---|---|
| `NoneTask`, `ServiceTask`, `UserTask`, `SendTask`, `ReceiveTask`, `ScriptTask`, `ManualTask`, `BusinessRuleTask` | Activity of that type | `Elements` |
| `EmbeddedSubProcess`, `EventSubProcess`, `CallActivity` | Activity subtype | `Elements` |
| `StartEvent` | `FlowEvent { Position = Start }` | `Elements` |
| `EndEvent` | `FlowEvent { Position = End }` | `Elements` |
| `FlowEvent` | `FlowEvent` (Position left at default) | `Elements` |
| `Message` | `Message` | `Messages` |
| `Signal` | `Signal` | `Signals` |
| `ErrorDefinition` | `ErrorDefinition` | `Errors` |
| `EscalationDefinition` | `EscalationDefinition` | `Escalations` |

`[DisplayName("…")]` on the property overrides `Name` with the attribute value.

## Entry Points — ProcessBuilder

The DSL is accessed through extension methods on `ProcessDefinition` in the static class `ProcessBuilder`:

```csharp
public static StartFlow Start(this ProcessDefinition definition);
public static StartFlow Start(this ProcessDefinition definition, Message message);
public static StartFlow Start(this ProcessDefinition definition, Signal signal);
public static StartFlow Start(this ProcessDefinition definition, TimerDefinition timer);
public static StartFlow Start(this ProcessDefinition definition, ConditionalDefinition condition)
```

Within a `ProcessDefinition` subclass constructor, call these as `this.Start()`.

## StartFlow

Defines the start of the process. Creates a `FlowEvent` with `Position = Start`.

```csharp
StartFlow
    .Go(Activity)          → ActivityBehavior
    .Await(EventBranch…)   → ProcessDefinition
```

### Example

```csharp
Start().Go(Draft)            // None Start Event → Draft activity
Start(Pay).Go(Processing)    // Message Start Event → Processing activity
```

## ActivityBehavior

The central DSL type for defining what happens on an `Activity`. Obtained from `ProcessBuilder.During(activity)`, `StartFlow.Go()`, `ParallelJoin.Go()`, `BoundaryCatch.Go()`, etc.

An `ActivityBehavior` controls **two independent concerns**:

1. **Outgoing path** — where the token goes after the activity completes. At most one may be defined; attempting a second throws `InvalidOperationException`.
2. **Boundary events** — events that fire during execution. Any number may be defined.

### Outgoing Path Methods

| Method | BPMN Semantics | Returns |
|--------|---------------|---------|
| `.Go(Activity)` / `.Go(FlowEvent)` | Unconditional sequence flow | `ActivityBehavior` |
| `.End()` | Anonymous End Event | `ActivityBehavior` |
| `.End(EndEvent)` | Named End Event | `ActivityBehavior` |
| `.Terminate()` | Terminate End Event | `ActivityBehavior` |
| `.Decide(Branch…)` | Exclusive Decision (XOR) | `ActivityBehavior` |
| `.Include(Branch…)` | Inclusive Decision (OR) | `InclusiveBranch` |
| `.Fork(FlowBranch…)` | Parallel Fork (AND) | `ParallelFork` |
| `.Await(EventBranch…)` | Event-based Decision | `ActivityBehavior` |

### Boundary Event Methods

| Method | BPMN Semantics | Returns |
|--------|---------------|---------|
| `.OnError<TException>()` | Error Boundary (always interrupting) | `BoundaryCatch` |
| `.OnError(ErrorDefinition)` | Error Boundary with definition | `BoundaryCatch` |
| `.OnTimer(TimeSpan)` | Timer Boundary | `BoundaryCatch` |
| `.OnMessage(Message)` | Message Boundary | `BoundaryCatch` |
| `.OnSignal(Signal)` | Signal Boundary | `BoundaryCatch` |
| `.OnCondition(ConditionalDefinition)` | Conditional Boundary | `BoundaryCatch` |
| `.OnEscalation(EscalationDefinition)` | Escalation Boundary | `BoundaryCatch` |
| `.OnCompensation()` | Compensation Boundary | `BoundaryCatch` |
| `.OnCancel()` | Cancel Boundary | `BoundaryCatch` |

### Mutual Exclusion Rules

The following combinations on the same activity are rejected by the validator:

| Combination | Valid? | Reason |
|-------------|:------:|--------|
| `.Go()` + `.Go()` | No | Only one unconditional flow |
| `.Go()` + `.Decide()` | No | Requires explicit gateway |
| `.Decide()` + `.Catch()` | Yes | Decision + boundary are independent |
| `.Go()` + `.Await()` | No | Outgoing path conflict |
| `.Fork()` + `.Catch()` | Yes | Fork + boundary are independent |

```csharp
During(Review).Go(Approve);         // unconditional flow
During(Draft).Decide(               // XOR decision
    When<Order>(o => o.Amount > 100).Go(Review),
    Otherwise().Go(Reject));
During(Processing).OnError<TimeoutException>().Go(Rejected);  // boundary
```

## Branch and FlowBranch

### Branch — Conditional

Used by `.Decide()` and `.Include()`. Created by `ProcessBuilder.When<T>()`, `ProcessBuilder.When(IConditionExpression)`, or `ProcessBuilder.Otherwise()`.

```csharp
public sealed class Branch
{
    Activity Entry { get; }              // first activity in branch
    Activity Exit { get; }              // last activity (updated by .Go)
    IConditionExpression? Condition { get; }
    bool IsDefault { get; }

    Branch Go(Activity target);          // chainable
}
```

### FlowBranch — Unconditional

Used by `.Fork()`. Implicitly convertible from a single `Activity` or from an `ActivityBehavior` chain.

```csharp
public sealed class FlowBranch
{
    Activity Entry { get; }    // Fork connects to this
    Activity Exit { get; }     // Join waits for this

    implicit operator FlowBranch(Activity a)
    implicit operator FlowBranch(ActivityBehavior d)
}
```

```csharp
// Single-step branch: ChargeCard becomes FlowBranch(ChargeCard, ChargeCard)
During(Prepare).Fork(ChargeCard, PackItems)

// Multi-step branch: During(PickItems).Go(PackItems).Go(PrintLabel)
// becomes FlowBranch(PickItems, PrintLabel)
During(Prepare).Fork(
    During(PickItems).Go(PackItems).Go(PrintLabel),
    ChargeCard)
```

### When\<T\> — Typed Conditions

`When<T>(Expression<Func<T, bool>>)` infers the variable key from the type name (converted to `snake_case` via Humanizer), deserializes the JSON value from process variables, and evaluates the compiled expression:

```csharp
When<Order>(o => o.Amount > 1000).Go(Review)
```

The variable key for this condition is `order` (from `Order` → `order`). At runtime, the engine looks up `order` in `ProcessInstance.Variables`, deserializes the JSON into an `Order` instance, and calls `o.Amount > 1000`.

## ParallelFork and ParallelJoin

`.Fork()` returns `ParallelFork`, which only exposes `.Join()` — the fork must be paired with a join:

```csharp
public sealed class ParallelFork
{
    ParallelJoin Join(params Activity[] exits);  // explicit
    ParallelJoin Join();                          // auto-derived from branches
}

public sealed class ParallelJoin
{
    ActivityBehavior Go(Activity target);
}
```

When `Join()` is called without arguments, the exit activities are automatically derived from the fork's branches.

```csharp
During(Prepare).Fork(
        During(PickItems).Go(PackItems).Go(PrintLabel),
        ChargeCard)
    .Join()                         // auto: waits for PrintLabel + ChargeCard
    .Go(Ship);
```

## InclusiveBranch and InclusiveMerge

`.Include()` returns `InclusiveBranch`, which optionally allows `.Merge()`:

```csharp
public sealed class InclusiveBranch
{
    InclusiveMerge Merge(params Activity[] exits);
}

public sealed class InclusiveMerge
{
    ActivityBehavior Go(Activity target);
}
```

If `.Merge()` is omitted, branches converge via implicit XOR merge (pass-through):

```csharp
During(Prepare).Include(
        When<Order>(o => o.NeedsPackaging).Go(Pack),
        When<Order>(o => o.NeedsInsurance).Go(Insure))
    .Merge()                        // synchronizing inclusive merge
    .Go(Ship);
```

## EventBranch — Event-based Gateway

Created by `ProcessBuilder.On()`, `OnTimer()`, `OnCondition()`:

```csharp
public sealed class EventBranch
{
    EventBranch Go(Activity target);
    EventBranch Go(EndEvent target);
    EventBranch Go(FlowEvent target);
    EventBranch Decide(params Branch[] branches);  // XOR after catch
}
```

Each `EventBranch` creates an `IntermediateCatch` event that waits for the specified trigger. The first branch whose event fires determines the path:

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

Created by boundary event methods on `ActivityBehavior`. Creates a `FlowEvent` with `Position = Boundary`, attaches it to the activity, and creates the outgoing flow:

```csharp
public sealed class BoundaryCatch
{
    ActivityBehavior Go(Activity target);
    ActivityBehavior Go(EndEvent target);
    BoundaryCatch NonInterrupting();     // SM validator rejects this
}
```

`.Go()` returns the original `ActivityBehavior` for chaining:

```csharp
During(Approve)
    .OnError<TimeoutException>().Go(TimedOut)
    .Await(                       // chained: boundary + event gateway
        On(Pay).Go(Fulfill),
        On(Cancel).Go(Cancelled));
```

## Standalone Join and Merge

For split-style definitions where fork and join are declared separately, `ProcessBuilder` provides top-level `Join()` and `Merge()`:

```csharp
During(Prepare).Fork(PickItems, ChargeCard);
During(PickItems).Go(PackItems).Go(PrintLabel);
Join(PrintLabel, ChargeCard).Go(Ship);
```

## Complete Type Chain

```
ProcessBuilder
    .Start() / .Start(Message) / .Start(Signal) / .Start(Timer)
        → StartFlow
          .Go(Activity)        → ActivityBehavior
          .Await(EventBranch…) → ProcessDefinition

    .During(Activity) → ActivityBehavior
      .Go(Activity)         → ActivityBehavior
      .End() / .End(EndEvent) → ActivityBehavior
      .Terminate()          → ActivityBehavior
      .Decide(Branch…)      → ActivityBehavior
      .Include(Branch…)     → InclusiveBranch → .Merge() → InclusiveMerge → .Go(Activity) → ActivityBehavior
      .Fork(FlowBranch…)    → ParallelFork → .Join() → ParallelJoin → .Go(Activity) → ActivityBehavior
      .Await(EventBranch…)  → ActivityBehavior
      .OnError<T>() / .OnTimer() / … → BoundaryCatch → .Go(Activity) → ActivityBehavior

    .Join(Activity…)         → ParallelJoin
    .Merge(Activity…)        → InclusiveMerge
    .When<T>(expr) / .Otherwise() → Branch → .Go(Activity) → Branch
    .On(Message) / .OnTimer() → EventBranch → .Go(Activity) / .Decide(Branch…)
```
