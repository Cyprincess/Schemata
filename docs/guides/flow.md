# Flow

Add a BPMN process engine to the Student app and model a small enrollment workflow with the
code-first C# DSL: a start event, a review activity, an exclusive decision, and two end events. This
guide builds on [Getting Started](getting-started.md).

## Add the package

`Schemata.Application.Complex.Targets` does not include the Flow packages. Add the foundation and
the default state-machine engine explicitly:

```shell
dotnet add package --prerelease Schemata.Flow.Foundation
dotnet add package --prerelease Schemata.Flow.StateMachine
```

A persistence provider is also required so `SchemataProcess`, `SchemataProcessToken`, and
`SchemataProcessSource` rows can be stored. `Schemata.Entity.Repository` plus an EF Core or
LinqToDB adapter is the typical choice.

## Enable the engine

Add `UseFlow()` and `UseStateMachine()` inside `UseSchemata`. The returned builder registers process
definitions at startup:

```csharp
schema.UseFlow()
      .UseStateMachine()
      .Use<EnrollmentProcess>();
```

`UseFlow` activates the flow feature; `UseStateMachine` registers the default engine. Without
`UseStateMachine`, the engine runtime is never registered and start calls resolve to nothing.
Feature priorities and engine keys are covered in [Flow Overview](../documents/flow/overview.md).

`Use<TProcess>()` returns the builder, so multiple processes chain; an optional
`Action<ProcessConfiguration>` configures a single definition:

```csharp
schema.UseFlow()
      .UseStateMachine()
      .Use<EnrollmentProcess>()
      .Use<GraduationProcess>();
```

## Define the process

Create `EnrollmentProcess.cs`. Subclass `ProcessDefinition`, declare each BPMN element as a
get-only property, and wire them in the constructor. The base constructor materializes the
properties through reflection; the fluent builder connects them.

```csharp
using Schemata.Abstractions.Entities;
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

// Application is a source entity. When<Application> requires ICanonicalName.
public sealed class Application : ICanonicalName
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
    public bool    Accepted      { get; set; }
}
```

The `Application` source type must implement `ICanonicalName`. The `When<Application>` constraint is
`where T : class, ICanonicalName`; the predicate evaluates against the source bound to the current
token under the binding name `application` (the type name lowercased and underscored).

DSL methods map to BPMN node types:

| Method                             | BPMN element                                                  |
| ---------------------------------- | ------------------------------------------------------------- |
| `this.Start()`                     | none start event                                              |
| `.Go(activity)`                    | unconditional sequence flow                                   |
| `.Decide(...)`                     | exclusive gateway (XOR)                                       |
| `this.When<T>(predicate)`          | guarded branch reading the source bound under the type's name |
| `this.Otherwise()`                 | default branch                                                |
| `.End(endEvent)` / `.Go(endEvent)` | route to an end event                                         |

`Otherwise` is taken when no guard matches.

## Persisted state

A running instance persists a `SchemataProcess` row for the aggregate lifecycle, one
`SchemataProcessToken` row per token position, and `SchemataProcessSource` rows for the bound
source entities that `When<T>` predicates read. The row schemas, lifecycle states, and column
semantics are in [Runtime Services](../documents/flow/runtime.md).

## Run it

```shell
dotnet run
```

Every registered definition is validated at startup, so an invalid process fails fast here rather
than on the first start call. The validation rules are in
[Flow Validation](../documents/flow/validator.md).

## Expose process management

Both transport extensions chain off the `SchemataFlowBuilder` that `UseFlow` returns. Add the
`Schemata.Flow.Http` package (or `Schemata.Flow.Grpc` for gRPC), then chain the map call:

```csharp
schema.UseFlow()
      .UseStateMachine()
      .MapHttp()
      .Use<EnrollmentProcess>();
```

This exposes the process verbs as AIP-136 custom methods: `POST ~/v1/processes:start`,
`POST ~/v1/processes/{name}:complete`, `POST ~/v1/processes/{name}:correlate`,
`POST ~/v1/processes:signal`, `POST ~/v1/processes/{name}:terminate`, plus read endpoints for
instances, tokens, and transition history. Or add gRPC:

```csharp
schema.UseFlow()
      .UseStateMachine()
      .MapGrpc()
      .Use<EnrollmentProcess>();
```

## Start and complete through HTTP

A start body follows the `StartProcessInstanceRequest` shape:

```
POST /v1/processes:start
Content-Type: application/json

{
  "definitionName": "EnrollmentProcess",
  "source": "applications/a1",
  "displayName": "...",
  "description": "...",
  "requestId": "..."
}
```

When `source` is present, the handler resolves the entity through `IRepository<T>` and binds it via
`IFlowRunner.StartAsync<TState>`. When `source` is absent, the handler falls through to the
sourceless overload. The endpoint responds `200 OK` with the process row.

A complete body follows `CompleteActivityRequest`:

```
POST /v1/processes/processes%2Fp1:complete
Content-Type: application/json

{ "token": "processes/p1/tokens/t1" }
```

`token` is optional under the state-machine engine because the process has exactly one token. The
BPMN engine requires it when more than one ready token exists.

## Start an instance from code

The HTTP and gRPC verbs cover process management end to end. When application code needs to start a
process as part of its own logic, inject `IFlowRunner`:

```csharp
using Schemata.Flow.Foundation;

// Sourceless variant.
var process = await runner.StartAsync("EnrollmentProcess", ct: ct);

// Variant binding a source entity. Application must implement ICanonicalName.
var process = await runner.StartAsync("EnrollmentProcess", application, ct: ct);
```

Both overloads accept an optional `StartProcessOptions` (`DisplayName`, `Description`)
and return the persisted `SchemataProcess` row. The principal enters only through the
resource-method handlers; the programmatic interface carries none.

## Next steps

- [Event Bus](event-bus.md) — bridge BPMN `Message` and `Signal` catches to the event bus
- [Scheduling](scheduling.md) — fire timer catches through the scheduler

## See also

- [Flow Overview](../documents/flow/overview.md) — architecture, the BPMN subset, startup
- [Flow DSL](../documents/flow/dsl.md) — the full code-first builder
- [Flow HTTP Transport](../documents/flow/http.md) — the process verbs and read endpoints
