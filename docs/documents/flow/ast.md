# Flow AST

The Flow AST is a strongly-typed BPMN 2.0 graph model. Element connections use object references
rather than string IDs, and event semantics decompose into orthogonal *position* and *definition*
axes. The model lives in `Schemata.Flow.Skeleton.Models` and is consumed by the DSL builders that
construct it and by the engine that traverses it.

The AST models more BPMN than the default `StateMachineEngine` executes. Parallel, inclusive, and
complex gateways, sub-processes, and multi-instance loops are all expressible; the state machine
rejects them at validation. They exist for alternate engines registered as keyed `IFlowRuntime`
services.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Skeleton` | `Models/FlowElement.cs`, `Models/Activity.cs`, `Models/Gateway.cs`, `Models/FlowEvent.cs`, `Models/SequenceFlow.cs`, `Models/IEventDefinition.cs`, `Models/ProcessDefinition.cs`, `Models/ProcessInstance.cs` |

## FlowElement

Every graph node derives from `FlowElement`:

```csharp
public abstract class FlowElement
{
    public string Id   { get; set; } = null!;
    public string Name { get; set; } = null!;
}
```

`Name` is the human-facing state label persisted to `SchemataProcess.State`. `Id` is the stable
key. When the DSL synthesizes an element, it assigns `Id` a GUID string
(`Identifiers.NewUid().ToString("n")`); when a magic property declares an element, `Name` defaults
to the property name and `Id` to a generated GUID.

## Activity hierarchy

`Activity` is the base for every task and sub-process:

```csharp
public abstract class Activity : FlowElement
{
    public LoopCharacteristics? LoopCharacteristics { get; set; }
    public bool                 IsForCompensation   { get; set; }
    public SequenceFlow?        DefaultFlow         { get; set; }
    public List<SequenceFlow>   Incoming            { get; } = [];
    public List<SequenceFlow>   Outgoing            { get; } = [];
}
```

```
Activity
    NoneTask  ServiceTask  UserTask  SendTask  ReceiveTask  ScriptTask  ManualTask  BusinessRuleTask
    SubProcess (abstract, adds bool TriggeredByEvent)
        EmbeddedSubProcess  EventSubProcess  TransactionSubProcess  AdHocSubProcess
    CallActivity (adds string CalledElement)   // target process definition name
```

All eight task types are bare subclasses; their type distinguishes the BPMN task kind. The state
machine treats every `Activity` identically — it runs the task and follows its outgoing flow.

## Gateway hierarchy

```csharp
public abstract class Gateway : FlowElement
{
    public List<SequenceFlow> Incoming { get; } = [];
    public List<SequenceFlow> Outgoing { get; } = [];
}
```

```
Gateway
    ExclusiveGateway
    ParallelGateway
    InclusiveGateway
    EventBasedGateway   (adds bool Parallel)     // false = exclusive event gateway
    ComplexGateway      (adds IConditionExpression? ActivationCount)
```

The state machine supports `ExclusiveGateway` and `EventBasedGateway` (with `Parallel == false`).
`ParallelGateway`, `InclusiveGateway`, and `ComplexGateway` are rejected by its validator.

## FlowEvent

A single `FlowEvent` type carries all event flavors; the `Position` and `Definition` fields select
the flavor:

```csharp
public class FlowEvent : FlowElement
{
    public EventPosition      Position     { get; set; }
    public IEventDefinition?  Definition   { get; set; }
    public bool               Interrupting { get; set; } = true;
    public Activity?          AttachedTo   { get; set; }    // set for boundary events
    public bool               IsTerminate  { get; set; }
    public List<SequenceFlow> Incoming     { get; } = [];
    public List<SequenceFlow> Outgoing     { get; } = [];
}
```

`StartEvent` and `EndEvent` are thin subclasses whose constructors set `Position` to `Start` and
`End`. They exist so a magic property declared as `public EndEvent Done { get; }` materializes a
`FlowEvent` with the right position.

### EventPosition

```csharp
public enum EventPosition
{
    Start,             // creates the token
    IntermediateCatch, // parks the token until a trigger arrives
    IntermediateThrow, // fires as the token passes through
    Boundary,          // attached to an Activity (AttachedTo)
    End,               // consumes the token
}
```

## IEventDefinition

A definition supplies the event's semantics. All definitions expose a `Name` used for trigger
matching:

```csharp
public interface IEventDefinition
{
    string Name { get; }
}
```

| Definition | Extra members |
| --- | --- |
| `NoneDefinition` | — |
| `Message` | `Type? PayloadType` |
| `Signal` | `Type? PayloadType` |
| `TimerDefinition` | `TimerType TimerType`, `string TimeExpression` |
| `ErrorDefinition` | `string? ErrorCode`, `Type ExceptionType` |
| `EscalationDefinition` | `string? EscalationCode` |
| `ConditionalDefinition` | `IConditionExpression Condition` |
| `CompensationDefinition` | `Activity? ActivityRef` |
| `CancelDefinition` | — |
| `LinkDefinition` | — |
| `MultipleDefinition` | `List<IEventDefinition> Definitions` |
| `ParallelDefinition` | `List<IEventDefinition> Definitions` |

```csharp
public enum TimerType { Date, Duration, Cycle }
```

A definition instance is reusable: the same `Message Pay` can appear in `Start(Pay)` and `On(Pay)`.
The position comes from the DSL context that places the event, not from the definition type.

## SequenceFlow

```csharp
public sealed class SequenceFlow
{
    public string                Id        { get; set; } = null!;
    public FlowElement           Source    { get; set; } = null!;
    public FlowElement           Target    { get; set; } = null!;
    public IConditionExpression? Condition { get; set; }
    public bool                  IsDefault { get; set; }
}
```

`Source` and `Target` are direct object references, so traversal matches by reference and there are
no dangling string IDs. A flow with a `Condition` is a guarded edge; the `IsDefault` flow is taken
when no guarded sibling matches.

## IConditionExpression

```csharp
public interface IConditionExpression
{
    ValueTask<bool> Evaluate(FlowConditionContext context);
}
```

The engine `await`s `Evaluate` — conditions are genuinely asynchronous. `LambdaConditionExpression`
wraps a `Func<FlowConditionContext, ValueTask<bool>>`. The DSL's typed `When<T>(...)` builds a
lambda that pulls the variable named after `T` (lowercased and underscored) out of
`FlowConditionContext.Variables`, deserializes it to `T`, and applies the predicate.

```csharp
public sealed class FlowConditionContext
{
    public ProcessDefinition           Definition   { get; set; } = null!;
    public ProcessInstance             Instance     { get; set; } = null!;
    public Dictionary<string, object?> Variables    { get; set; } = [];
    public string                      CurrentState { get; set; } = null!;
}
```

## ProcessDefinition

```csharp
public class ProcessDefinition
{
    public string  Name        { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }

    public List<FlowElement>          Elements    { get; } = [];
    public List<SequenceFlow>         Flows       { get; } = [];
    public List<Message>              Messages    { get; } = [];
    public List<Signal>               Signals     { get; } = [];
    public List<ErrorDefinition>      Errors      { get; } = [];
    public List<EscalationDefinition> Escalations { get; } = [];
}
```

The base constructor calls `InitializeProperties()`, which scans the concrete subclass for **magic
properties**: public auto-properties that are `null` and whose type is a declarable element kind
(any non-abstract `Activity`, `StartEvent`, `EndEvent`, `FlowEvent`, `Message`, `Signal`,
`ErrorDefinition`, `EscalationDefinition`). For each it constructs an instance and writes it back
through the compiler-generated backing field, setting `Name` to the property name (or to
`[DisplayName("...")]` when present) and `Id` to a generated GUID. See [DSL Reference](dsl.md) for
how the fluent builder then wires these elements into `Elements` and `Flows`.

## ProcessInstance

The engine's per-call output. It carries no identity of its own beyond the canonical name the
runtime stamps onto it:

```csharp
public sealed class ProcessInstance : ICanonicalName
{
    public string                      StateId     { get; set; } = null!;
    public string?                     State       { get; set; }
    public string?                     WaitingAtId { get; set; }
    public string?                     WaitingAt   { get; set; }
    public bool                        IsComplete  { get; set; }
    public Dictionary<string, object?> Variables   { get; set; } = new();

    public string?                     Name          { get; set; }
    public string?                     CanonicalName { get; set; }
}
```

`WaitingAt` / `WaitingAtId` distinguish "at an activity, ready to auto-advance" from "parked at an
event-based gateway or an intermediate catch, waiting for a trigger". When they are set,
`AdvanceAsync` returns the instance unchanged and `TriggerAsync` uses `WaitingAtId` as the primary
lookup key.

## Extension points

- Implement `IConditionExpression` to plug in a custom guard evaluator (for example CEL or AIP-160).
- Implement `IFlowRuntime` to execute the parts of the AST the state machine does not (parallel
  flow, sub-processes, compensation).

## Caveats

- `InitializeProperties()` writes through compiler-generated `<Name>k__BackingField` fields, so a
  magic property must be a plain auto-property. Hand-written backing fields are not discovered.
- `TimerDefinition.TimeExpression` is a raw string. The state machine does not parse it;
  `Schemata.Flow.Scheduling` converts it through `TimerDefinitionConverter.ToSchedule`.

## See also

- [DSL Reference](dsl.md)
- [Engine](engine.md)
- [Validator](validator.md)
- [Scheduling Integration](scheduling.md)
