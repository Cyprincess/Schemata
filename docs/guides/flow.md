# Flow

Add a BPMN process engine to the Student app and model a small enrollment workflow with the
code-first C# DSL: a start event, a review activity, an exclusive decision, and two end events. This
guide builds on [Getting Started](getting-started.md).

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Flow.Foundation`. To compose
packages by hand:

```shell
dotnet add package --prerelease Schemata.Flow.Foundation
```

## Enable the engine

Add `UseFlow()` inside `UseSchemata`. The returned builder registers process definitions at startup:

```csharp
schema.UseFlow()
      .Use<EnrollmentProcess>();
```

`UseFlow` activates `SchemataFlowFeature` (priority `480_000_000`). `Use<TProcess>()` registers
a `ProcessDefinition` subclass against the default engine, the single-token state machine
(`SchemataConstants.FlowEngines.StateMachine`). The state machine is wired by `UseFlow` itself, so no
extra call is needed to turn it on.

`Use<TProcess>()` returns the builder, so multiple processes chain. Pass an optional
`Action<ProcessConfiguration>` to configure a single definition without breaking the chain:

```csharp
schema.UseFlow()
      .Use<EnrollmentProcess>()
      .Use<GraduationProcess>(c => c.WithAuthorization());
```

## Define the process

Create `EnrollmentProcess.cs`. Subclass `ProcessDefinition`, declare each BPMN element as a get-only
property, and wire them in the constructor. The base constructor materializes the properties; the
fluent builder connects them.

```csharp
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;

public sealed class EnrollmentProcess : ProcessDefinition
{
    public UserTask Review   { get; } = null!;
    public EndEvent Approved { get; } = null!;
    public EndEvent Rejected { get; } = null!;

    public EnrollmentProcess()
    {
        this.Start().Go(Review);

        this.During(Review).Decide(
            this.When<Application>(a => a.Accepted).Go(Approved),
            this.Otherwise().Go(Rejected));
    }
}

public sealed class Application
{
    public bool Accepted { get; set; }
}
```

Each property's name becomes the state label persisted to `SchemataProcess.State` — here `Review`,
`Approved`, `Rejected`. The DSL methods map to BPMN node types:

| Method | BPMN element |
| --- | --- |
| `this.Start()` | none start event |
| `.Go(activity)` | unconditional sequence flow |
| `.Decide(...)` | exclusive gateway (XOR) |
| `this.When<T>(predicate)` | guarded branch reading variable `t` |
| `this.Otherwise()` | default branch |
| `.End(endEvent)` / `.Go(endEvent)` | route to an end event |

`When<Application>(a => a.Accepted)` reads the process variable named `application` (the type name,
lowercased and underscored), deserializes it to `Application`, and evaluates the predicate. The
`Otherwise` branch is taken when no guard matches.

## Associate the process with a Student

To tie a process instance to the `Student` entity that started it, register the definition with the
default engine and stamp the source entity when you start an instance:

```csharp
schema.UseFlow().Use<EnrollmentProcess>();
```

`IProcessRuntime.StartProcessInstanceAsync` accepts a `sourceEntity` argument that records an
`ISourceReference` on the `SchemataProcess` row, so the instance can be traced back to the `Student`.

## Run it

```shell
dotnet run
```

`SchemataFlowFeature` registers and validates `EnrollmentProcess` at startup; an invalid definition
fails fast here. Process instances are created and advanced through `IProcessRuntime`, or over HTTP
and gRPC once you add a transport.

## Expose process management

Both transport extensions chain off the `SchemataFlowBuilder` that `UseFlow` returns. Add HTTP:

```csharp
schema.UseFlow()
      .MapHttp()
      .Use<EnrollmentProcess>();
```

This exposes the process verbs as AIP-136 custom methods — `POST ~/v1/processes:start`,
`POST ~/v1/processes/{name}:complete`, and so on — plus read endpoints for instances and their
transition history. Or add gRPC:

```csharp
schema.UseFlow()
      .MapGrpc()
      .Use<EnrollmentProcess>();
```

## Next steps

- [Event Bus](event-bus.md) — bridge BPMN `Message` and `Signal` catches to the event bus
- [Scheduling](scheduling.md) — fire timer catches through the scheduler

## See also

- [Flow Overview](../documents/flow/overview.md) — architecture, the BPMN subset, startup
- [Flow DSL](../documents/flow/dsl.md) — the full code-first builder
- [Flow HTTP Transport](../documents/flow/http.md) — the process verbs and read endpoints
