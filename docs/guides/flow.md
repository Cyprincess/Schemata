# Flow

Add a BPMN 2.0 process engine to the Student CRUD app. This guide shows how to define a minimal enrollment process with a start event, an activity, an exclusive gateway, and an end event using the code-first C# DSL. This guide builds on [Getting Started](getting-started.md).

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Flow.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Flow.Foundation
```

## Enable the flow engine

Add `UseFlow()` in `Program.cs`. `SchemataFlowFeature` runs at `Order = Priority = 480_000_000`. The optional delegate pre-registers process definitions at startup:

```csharp
schema.UseFlow(flow => {
    flow.Use<EnrollmentProcess>();
});
```

`UseFlow` accepts `Action<FlowBuilder>?`. `FlowBuilder.Use<TProcess>()` registers a `ProcessDefinition` subclass with the engine named in `SchemataConstants.FlowEngines.StateMachine` (the default key). `SchemataFlowFeature` wires `Schemata.Flow.StateMachine` into that keyed-singleton slot, so `UseFlow()` alone is enough — no extra `Use*` call is needed to enable the state-machine runtime.

## Define the process

Create `EnrollmentProcess.cs`. Subclass `ProcessDefinition` and build the graph using the C# DSL:

```csharp
using Schemata.Flow.Skeleton.Models;

public sealed class EnrollmentProcess : ProcessDefinition
{
    public EnrollmentProcess()
    {
        var start    = StartEvent("start");
        var review   = UserTask("review", "Review application");
        var gateway  = ExclusiveGateway("decision");
        var approved = EndEvent("approved");
        var rejected = EndEvent("rejected");

        start   .SequenceFlow(review);
        review  .SequenceFlow(gateway);
        gateway .SequenceFlow(approved, condition: ctx => ctx.Get<bool>("accepted"));
        gateway .SequenceFlow(rejected);
    }
}
```

The DSL mirrors BPMN 2.0.2 node types. Supported elements include:

| Method | BPMN element |
| ------ | ------------ |
| `StartEvent(id)` | Start event |
| `EndEvent(id)` | End event |
| `UserTask(id, name)` | User task |
| `ServiceTask(id, name)` | Service task |
| `ExclusiveGateway(id)` | Exclusive gateway (XOR) |
| `EventBasedGateway(id)` | Event-based gateway |
| `SequenceFlow(target, condition?)` | Sequence flow with optional guard |

## Associate the process with Student

To tie a process instance to a `Student` entity, use the two-type overload:

```csharp
schema.UseFlow(flow => {
    flow.Use<EnrollmentProcess, Student>();
});
```

This stores the entity type on `ProcessConfiguration.EntityType` so the flow runtime can correlate process instances with entity records.

## Verify

```shell
dotnet run
```

The flow engine is now active. Process instances are created and advanced via the `IProcessService` interface or the HTTP/gRPC transport layers (see [Flow HTTP](../documents/flow/http.md) and [Flow gRPC](../documents/flow/grpc.md)).

To expose process management over HTTP, add `UseFlowHttp()`:

```csharp
schema.UseFlow(flow => flow.Use<EnrollmentProcess, Student>())
      .UseFlowHttp();
```

To expose it over gRPC, add `UseFlowGrpc()`:

```csharp
schema.UseFlow(flow => flow.Use<EnrollmentProcess, Student>())
      .UseFlowGrpc();
```

## See also

- [Multi-Tenancy](multi-tenancy.md) — previous in the series: per-tenant process isolation
- [Event Bus](event-bus.md) — next in the series: BPMN `Message` and `Signal` catches bridge to the event bus
- [Flow Overview](../documents/flow/overview.md) — BPMN subset, engine architecture
- [Flow DSL](../documents/flow/dsl.md) — code-first process graph construction
- [Flow State Machine](../documents/flow/state-machine.md) — default engine wiring
