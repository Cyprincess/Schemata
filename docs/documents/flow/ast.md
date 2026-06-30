# Flow AST

The Flow AST is a strongly-typed BPMN 2.0 graph model. Element connections use object references
rather than string IDs, and event semantics decompose into orthogonal _position_ and _definition_
axes. The model lives in `Schemata.Flow.Skeleton.Models` and is consumed by the DSL builders that
construct it and by the engines that traverse it.

The AST models more BPMN than the default `StateMachineEngine` executes. Parallel, inclusive, and
complex gateways, sub-processes, multi-instance loops, and compensation are all expressible; the
state machine rejects them at validation. They exist for alternate engines registered as keyed
`IFlowRuntime` services.

## Where the code lives

| Package                  | Key files                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Flow.Skeleton` | `Models/FlowElement.cs`, `Models/Activity.cs`, `Models/Gateway.cs`, `Models/FlowEvent.cs`, `Models/StartEvent.cs`, `Models/EndEvent.cs`, `Models/SequenceFlow.cs`, `Models/IEventDefinition.cs`, `Models/Message.cs`, `Models/Signal.cs`, `Models/ProcedureTask.cs`, `Models/SubProcess.cs`, `Models/EmbeddedSubProcess.cs`, `Models/EventSubProcess.cs`, `Models/TransactionSubProcess.cs`, `Models/AdHocSubProcess.cs`, `Models/CallActivity.cs`, `Models/LoopCharacteristics.cs`, `Models/StandardLoopCharacteristics.cs`, `Models/MultiInstanceLoopCharacteristics.cs`, `Models/MIEventBehavior.cs`, `Models/ProcessDefinition.cs`, `Models/ProcessSnapshot.cs`, `Models/ProcessConfiguration.cs`, `Models/ProcessDefinitionInfo.cs`, `Models/TargetState.cs`, `Models/TokenSnapshot.cs`, `Models/TransitionKind.cs`, `Models/*Request.cs` |

## FlowElement

Every graph node derives from `FlowElement`:

```csharp
public abstract class FlowElement
{
    public string Name { get; set; } = null!;
}
```

`Name` is the canonical element identity. It must be non-empty and unique within a definition
(enforced by `ProcessStructureValidator.ValidateElementNames`). It is deterministic across
definition rebuilds, so a process persisted by one host resumes on another host or after a restart
because the rebuilt definition produces identical element names. `Name` is also the resume key
persisted on `SchemataProcessToken.StateName` and `WaitingAtName`, and the label surfaced in
transition rows.

When a magic property declares an element, `Name` defaults to the property name (or to
`[DisplayName("...")]` when present). DSL-synthesized elements derive deterministic names from
their structural position:

| Synthesized element | Name pattern |
| ------------------------------------ | ----------------------------------------------------------------- |
| Start event (none) | `Start` |
| Start event (triggered) | `Start_{eventDefinition}` |
| Start await gateway | `Await_{startEvent}` |
| Anonymous end event | `End_{source}` |
| Terminate end event | `Terminate_{source}` |
| Exclusive decision gateway | `Decision_{source}` |
| Parallel fork gateway | `Fork_{source}` |
| Event-based gateway | `Await_{source}` (source = current chain tail) |
| Fork continuation join | `Join_{forkGateway}` |
| Inclusive continuation merge | `Merge_{decisionGateway}` |
| Root-level `Join(exits)` | `Join_{key}` where key = ordinally-sorted, length-prefixed exit names (`{len}:{name}` joined by `_`) |
| Root-level `Merge(exits)` | `Merge_{key}` (same key derivation) |
| Event-branch catch event | `Catch_{gateway}_{eventDefinition}` |
| Post-catch decision gateway | `Decision_{catchEvent}` |
| Boundary catch event | `Catch_{hostActivity}_{eventDefinition}` |
| Enter procedure task | `Enter_{activity}` |
| Leave procedure task | `Leave_{tail}` |
| Anonymous `When` branch task | `Branch_{gateway}_{index}` |
| `Otherwise` branch task | `Branch_{gateway}_Default` |

## Activity hierarchy

`Activity` is the base for every task and sub-process:

```csharp
public abstract class Activity : FlowElement
{
    public LoopCharacteristics? LoopCharacteristics { get; set; }
    public SequenceFlow?        DefaultFlow         { get; set; }
    public List<SequenceFlow>   Incoming            { get; } = [];
    public List<SequenceFlow>   Outgoing            { get; } = [];
}
```

```
Activity
    NoneTask  ServiceTask  UserTask  SendTask  ReceiveTask  ScriptTask  ManualTask  BusinessRuleTask
    ProcedureTaskBase (abstract, runtime-executable)
        ProcedureTask                  (Func<FlowTaskContext, ValueTask> Body)
        ProcedureTask<TPayload>        (Func<FlowTaskContext, TPayload, ValueTask> Body)
    SubProcess (abstract, adds bool TriggeredByEvent, List<FlowElement> Children, List<SequenceFlow> ChildFlows)
        EmbeddedSubProcess  EventSubProcess  TransactionSubProcess  AdHocSubProcess
    CallActivity (adds string CalledElement)   // target process definition name
```

All eight task types are bare subclasses; their type distinguishes the BPMN task kind. The state
machine treats every `Activity` identically except for `ProcedureTask` and
`ProcedureTask<TPayload>`, which carry an executable delegate.

`ProcedureTask<TPayload>` carries a typed payload. The `ProcedureTaskPayloadValidator` enforces
that a typed task is reachable only through matching typed message or signal catches; mismatches
raise `InvalidOperationException` at registration.

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

| Definition               | Extra members                                   |
| ------------------------ | ----------------------------------------------- |
| `NoneDefinition`         | (none)                                          |
| `Message`                | `Type? PayloadType`, settable `Name`            |
| `Message<TPayload>`      | typed variant; `PayloadType = typeof(TPayload)` |
| `Signal`                 | `Type? PayloadType`, settable `Name`            |
| `Signal<TPayload>`       | typed variant; `PayloadType = typeof(TPayload)` |
| `TimerDefinition`        | `TimerType TimerType`, `string TimeExpression`  |
| `ErrorDefinition`        | `string? ErrorCode`, `Type ExceptionType`       |
| `EscalationDefinition`   | `string? EscalationCode`                        |
| `ConditionalDefinition`  | `IConditionExpression Condition`                |
| `CompensationDefinition` | `Activity? Activity`                            |
| `CancelDefinition`       | (none)                                          |
| `LinkDefinition`         | (none)                                          |
| `MultipleDefinition`     | `List<IEventDefinition> Definitions`            |
| `ParallelDefinition`     | `List<IEventDefinition> Definitions`            |

```csharp
public enum TimerType { Date, Duration, Cycle }
```

A definition instance is reusable: the same `Message Pay` can appear in `Start(Pay)` and `On(Pay)`.
The position comes from the DSL context that places the event, not from the definition type.

The magic-property discoverer instantiates `Message<TPayload>` and `Signal<TPayload>` from
auto-properties declared on the definition; both subtypes land on the same `Messages` / `Signals`
list as their base types.

## SequenceFlow

```csharp
public sealed class SequenceFlow
{
    public FlowElement           Source    { get; set; } = null!;
    public FlowElement           Target    { get; set; } = null!;
    public IConditionExpression? Condition { get; set; }
    public bool                  IsDefault { get; set; }
}
```

`Source` and `Target` are direct object references, so traversal matches by reference. Sequence
flows carry no identity of their own; validation errors identify a flow by its source and target
element names. A flow with a `Condition` is a guarded edge; the `IsDefault` flow is taken when no
guarded sibling matches.

## IConditionExpression

```csharp
public interface IConditionExpression
{
    ValueTask<bool> Evaluate(FlowConditionContext context);
}
```

The engine `await`s `Evaluate`, so conditions are genuinely asynchronous.
`LambdaConditionExpression` wraps a `Func<FlowConditionContext, ValueTask<bool>>`. The DSL's
typed `When<T>(...)` builds a `SourceConditionExpression<TSource>` that resolves the source
binding named after `T` (`typeof(T).Name.Underscore().ToLowerInvariant()`, e.g. `Order` →
`order`, `OrderRequest` → `order_request`) and applies the predicate.
`When<TSource, TPayload>(Message<TPayload>, ...)` builds a `SourcePayloadConditionExpression<TSource, TPayload>`.
`When<T>("state == 'paid'")` and `When<T>(name, expression)` build a
`SourceStringConditionExpression<TSource>` carrying the raw expression text; the registry compiles
it at registration with the keyed `IExpressionCompiler` selected by
`ProcessConfiguration.Language` (for example `ExpressionLanguages.Cel`).

```csharp
using System;
using System.Collections.Generic;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

public sealed class FlowConditionContext
{
    public ProcessDefinition    Definition   { get; set; } = null!;
    public TokenSnapshot        Token        { get; set; } = null!;
    public SchemataProcess?     Process      { get; set; }
    public SchemataProcessToken? TokenEntity { get; set; }
    public IUnitOfWork?         UnitOfWork   { get; set; }
    public object?              Payload      { get; set; }
    public Dictionary<string, int> Bookkeeping { get; set; } = [];
    public string               CurrentState { get; set; } = null!;
    public required IServiceProvider Services { get; set; }

    public FlowTaskContext CreateTaskContext();
}
```

`CreateTaskContext()` materializes a `FlowTaskContext` for source-aware conditions, exposing
`SourceAsync<TEntity>(string?, ...)`, `Repository<TEntity>()`, and the general
`GetService<TService>(object? key)` / `GetRequiredService<TService>(object? key)` resolvers.
A resolved `IRepository` is enlisted in the current unit of work before it is returned.

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
(any non-abstract `Activity` subtype, `StartEvent`, `EndEvent`, `FlowEvent`, `Message`,
`Message<TPayload>`, `Signal`, `Signal<TPayload>`, `ErrorDefinition`, `EscalationDefinition`).
For each it constructs an instance and writes it back through the compiler-generated backing field,
setting `Name` to the property name (or to `[DisplayName("...")]` when present). The `Elements`
list receives every flow element; `Messages` and `Signals` receive the matching event definitions.
Pre-initialized element, message, signal, error, and escalation properties are also registered into
the definition collections (reference-guarded) with their `Name` defaulted to the property name
when empty.

`ProcessDefinition` also exposes rebuilt graph views:

- `AllElements`. Every flow element reachable from the root, recursing into `SubProcess.Children`.
- `AllFlows`. Every sequence flow reachable from the root, recursing into `SubProcess.ChildFlows`.
- `ByName`. `IReadOnlyDictionary<string, FlowElement>` keyed by `Name`.
- `OutgoingBySource` and `IncomingByTarget`. Adjacency maps keyed by `FlowElement` reference.

See [DSL Reference](dsl.md) for how the fluent builder wires these elements into `Elements` and
`Flows`. `FindElementByName` looks up a single element by name from `ByName`.

## ProcessSnapshot

The engine's per-call output carries the mutated process row, token rows, and transition rows. The
Foundation runner persists those rows under its unit of work:

```csharp
using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Entities;

public sealed class ProcessSnapshot : ICanonicalName
{
    public required SchemataProcess                          Process     { get; init; }
    public required IReadOnlyList<SchemataProcessToken>      Tokens      { get; init; }
    public required IReadOnlyList<SchemataProcessTransition> Transitions { get; init; }
}
```

`Tokens` carries the full live + historical token set after the operation; the state-machine engine
always returns a single token, the BPMN engine returns N. `Transitions` carries the new rows
emitted by the operation. `Process` is mutated in place by the engine. `Name` and `CanonicalName`
are delegated to the underlying process entity.

`SchemataProcessToken.WaitingAtName` distinguishes "at an activity, ready to auto-advance" from
"parked at an event-based gateway or an intermediate catch, waiting for a trigger". When it is set,
`AdvanceAsync` leaves that token parked and `TriggerAsync` uses `WaitingAtName` as the primary
lookup key. A token parked at a gateway reached from an activity keeps the business state name on
`StateName`; the gateway name lives only on `WaitingAtName`.

## Engine-neutral types

The skeleton exposes several types shared by both engines:

- `TargetState` (record). Engine-internal "next hop" with `StateName`, `WaitingAtName`, `IsComplete`.
- `TokenSnapshot`. Immutable read-only view of a single token; used in observer hooks and
  transport responses.
- `TransitionKind` — enum `Move`, `Cancel`, `Fail`, `Fork`, `Join`, `Spawn`, `Compensate`. The
  state-machine engine writes only `Move`, `Cancel`, and `Fail`; the BPMN engine adds the rest.
- `MIEventBehavior` — enum `None`, `One`, `All`, `Complex`. Multi-instance aggregation mode.

## Request DTOs

`StartProcessInstanceRequest`, `CompleteActivityRequest`, `CorrelateMessageRequest`, and
`ThrowSignalRequest` carry transport payloads into the engine. The Foundation handlers translate
them into engine calls.

## ProcessConfiguration

```csharp
using System;
using Schemata.Abstractions;

public sealed class ProcessConfiguration
{
    public string  Name                 { get; set; } = null!;
    public string  Engine               { get; set; } = SchemataConstants.FlowEngines.StateMachine;
    public Type?   DefinitionType       { get; set; }
    public string? Language             { get; set; }
    public bool    RequiresAuthorization{ get; set; }

    public ProcessConfiguration WithAuthorization();
}
```

`ProcessRegistration` extends this with `SourceTypes` (source bindings keyed by binding name, each
a `FlowSourceDescriptor`), `MessagePayloadTypes` (message name → CLR type), and `SignalPayloadTypes`
(signal name → CLR type).

## Extension points

- Implement `IConditionExpression` to plug in a custom guard evaluator (for example CEL or
  AIP-160).
- Implement `IFlowRuntime` to execute the parts of the AST the state machine does not (parallel
  flow, sub-processes, compensation).

## Caveats

- `InitializeProperties()` writes through compiler-generated `<Name>k__BackingField` fields, so a
  magic property must be a plain auto-property. Hand-written backing fields are not discovered.
- `TimerDefinition.TimeExpression` is a raw string. The state machine does not parse it;
  `Schemata.Flow.Scheduling` converts it through `TimerDefinitionConverter.ToSchedule`.
- `ProcessSnapshot.Process` is mutated in place. Treat the snapshot as read-only after the engine
  returns it; only the handler should persist or modify it.

## See also

- [DSL Reference](dsl.md)
- [Engine](engine.md)
- [Validator](validator.md)
- [Scheduling Integration](scheduling.md)
