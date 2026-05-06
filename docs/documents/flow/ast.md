# Flow AST

> **Source:** `Schemata.Flow.Skeleton/Models/`

The Flow AST is a strongly-typed BPMN 2.0.2 graph model where all element connections use object references rather than string IDs, and event semantics are decomposed into orthogonal *position* × *definition* axes.

See also [BPMN 2.0.2](https://www.omg.org/spec/BPMN/2.0.2/) §8.3 and §10.

## FlowElement

```csharp
public abstract class FlowElement
{
    public string Id { get; set; }
    public string Name { get; set; }
}
```

All graph nodes inherit from `FlowElement`. The `Name` doubles as the process instance state label during execution.

## Activity Hierarchy

```csharp
Activity : FlowElement
    LoopCharacteristics?       // StandardLoop or MultiInstance
    IsForCompensation : bool
    DefaultFlow? : SequenceFlow
    Incoming / Outgoing    // available for reverse-edge traversal

    NoneTask          : Activity   // generic BPMN task
    ServiceTask       : Activity   // external service invocation
    UserTask          : Activity   // human interaction
    SendTask          : Activity   // send message
    ReceiveTask       : Activity   // wait for message (can instantiate)
    ScriptTask        : Activity   // script execution
    ManualTask        : Activity   // human, no system assistance
    BusinessRuleTask  : Activity   // DMN or business rule evaluation

    SubProcess        : Activity (abstract)
        TriggeredByEvent : bool    // true = EventSubProcess
        EmbeddedSubProcess
        EventSubProcess
        TransactionSubProcess
        AdHocSubProcess

    CallActivity      : Activity
        CalledElement : string     // target ProcessDefinition name
```

## Gateway Hierarchy

```csharp
Gateway : FlowElement
    ExclusiveGateway   // XOR Decision / Merge (pass-through)
    ParallelGateway    // AND Fork / Join
    InclusiveGateway   // OR Decision / Merge (synchronizing)
    EventBasedGateway  // event-based Decision
        Parallel : bool  // false = exclusive, true = parallel
    ComplexGateway
        ActivationCount? : IConditionExpression
```

**BPMN terminology:**

| Gateway | Split name | Join name |
|---------|-----------|-----------|
| Parallel (AND) | **Fork** | **Join** |
| Exclusive (XOR) | **Decision** | **Merge** (pass-through) |
| Inclusive (OR) | **Decision** | **Merge** (synchronizing) |
| Event-based | **Decision** | — |

## FlowEvent

```csharp
public class FlowEvent : FlowElement
{
    EventPosition Position          // Start / Catch / Throw / Boundary / End
    IEventDefinition? Definition    // Message, Timer, Error, …
    bool Interrupting = true        // Boundary events: cancel or not
    Activity? AttachedTo            // Boundary events: parent activity
    bool IsTerminate = false        // End events: terminate vs plain
}
```

`StartEvent` and `EndEvent` are thin subclasses whose constructors set `Position` to the appropriate value — this exists solely for the magic property discovery pattern: declaring `public EndEvent Foo { get; private set; }` on a `ProcessDefinition` subclass causes the base constructor to create a `FlowEvent` with `Position = End`.

### EventPosition

```csharp
public enum EventPosition
{
    Start,              // creates a new token
    IntermediateCatch,  // waits for a trigger
    IntermediateThrow,  // fires when token arrives
    Boundary,           // attached to an Activity
    End,                // consumes the token
}
```

## IEventDefinition Hierarchy

```csharp
IEventDefinition
    Name : string                   // used for trigger matching at runtime

    NoneDefinition                 // no trigger — plain start/end
    Message { PayloadType? : Type } // point-to-point
    Signal  { PayloadType? : Type } // broadcast
    TimerDefinition {               // timer
        TimerType : Date|Duration|Cycle
        TimeExpression : string     // ISO 8601 / cron
    }
    ErrorDefinition {              // error (always interrupting)
        ErrorCode? : string
        ExceptionType : Type
    }
    EscalationDefinition { EscalationCode? : string }
    ConditionalDefinition { Condition : IConditionExpression }
    CompensationDefinition { ActivityRef? : Activity }
    CancelDefinition
    LinkDefinition                 // off-page connector
    MultipleDefinition { Definitions : List<IEventDefinition> }
    ParallelDefinition { Definitions : List<IEventDefinition> }
```

Event definitions are **reusable**: the same `Message Pay` instance can appear in both `Start(Pay)` and `On(Pay)`. Position is determined by the DSL usage context.

### BPMN Event Compatibility

The AST itself does not restrict combinations. Validators enforce compatibility per engine type.

| Definition \ Position | Start | Catch | Boundary | Throw | End |
|---|:---:|:---:|:---:|:---:|:---:|
| None | Y | — | — | — | Y |
| Message | Y | Y | Y | Y | Y |
| Timer | Y | Y | Y | — | — |
| Error | — | — | Y | — | Y |
| Escalation | Y | — | Y | Y | Y |
| Signal | Y | Y | Y | Y | Y |
| Compensation | — | — | Y | Y | Y |
| Conditional | Y | Y | Y | — | — |
| Cancel | — | — | Y | — | Y |
| Link | — | Y | — | Y | — |

## SequenceFlow

```csharp
public sealed class SequenceFlow
{
    string Id
    FlowElement Source            // object reference, not string ID
    FlowElement Target            // object reference, not string ID
    IConditionExpression? Condition
    bool IsDefault
}
```

Unlike the legacy implementation, `Source` and `Target` hold direct object references. This enables reference-based matching during engine traversal and eliminates dangling string references.

## IConditionExpression

```csharp
public interface IConditionExpression
{
    ValueTask<bool> Evaluate(FlowConditionContext context);
}
```

`LambdaConditionExpression` wraps a `Func<FlowConditionContext, ValueTask<bool>>`. For typed conditions (`When<T>(…)`), the lambda extracts the variable of type `T` from `Variables` by the parameter name inferred from the expression, deserializes from JSON, and evaluates the compiled predicate.

## LoopCharacteristics

```csharp
StandardLoopCharacteristics : LoopCharacteristics
    LoopCondition? : IConditionExpression
    TestBefore : bool       // false = do-while, true = while-do
    LoopMaximum? : int

MultiInstanceLoopCharacteristics : LoopCharacteristics
    LoopCardinality? : IConditionExpression
    CompletionCondition? : IConditionExpression
    IsSequential : bool
    OneCompletedEventBehavior : MIEventBehavior (None|One|All|Complex)
```

## ProcessDefinition

```csharp
public class ProcessDefinition
{
    string Name
    string? DisplayName
    string? Description

    List<FlowElement>  Elements      // all nodes: Activities, Events, Gateways
    List<SequenceFlow> Flows         // all directed edges
    List<Message>      Messages
    List<Signal>       Signals
    List<ErrorDefinition>   Errors
    List<EscalationDefinition> Escalations
}
```

The base constructor calls `InitializeProperties()`, which scans the concrete subclass for **magic properties**: public auto-properties whose value is `null` and whose type is one of the known declarable types (`Activity` subclasses, `StartEvent`, `EndEvent`, `FlowEvent`, `Message`, `Signal`, `ErrorDefinition`, `EscalationDefinition`). For each, it creates an instance and writes it back via the compiler-generated backing field.

## ProcessInstance

```csharp
public sealed class ProcessInstance
{
    string  State                    // current element name
    string? WaitingAt                // EventBasedGateway or IntermediateCatchEvent
    bool    IsComplete
    Dictionary<string, object?> Variables
}
```

`WaitingAt` distinguishes "Activity executing" from "waiting for event trigger":

- When `WaitingAt` is set, `AdvanceAsync` returns early — cannot auto-advance.
- `TriggerAsync` checks `WaitingAt` first when resolving event matches.
- API consumers can query this to determine if the instance needs input.
