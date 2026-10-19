# Flow

Add a BPMN process engine to the Student app and model a small enrollment workflow with the
code-first C# DSL: a start event, a review activity, an exclusive decision, and two end events. This
is a feature branch after [Multi-Tenancy](multi-tenancy.md): it reuses the established `Student`
repository and works when you skip the tenancy branch.

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

## Register tenant-owned rows

Tenant isolation is driven by the `ITenant` trait, and the shipped Flow entities do not implement
it. A tenancy host declares one subtype per persisted row and closes the engine on them:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Entities;

public sealed class TenantProcess : SchemataProcess, ITenant { public Guid? TenantUid { get; set; } }
public sealed class TenantProcessToken : SchemataProcessToken, ITenant { public Guid? TenantUid { get; set; } }
public sealed class TenantProcessTransition : SchemataProcessTransition, ITenant { public Guid? TenantUid { get; set; } }
public sealed class TenantProcessCompensation : SchemataProcessCompensation, ITenant { public Guid? TenantUid { get; set; } }
public sealed class TenantProcessParticipant : SchemataProcessParticipant, ITenant { public Guid? TenantUid { get; set; } }
public sealed class TenantProcessSource : SchemataProcessSource, ITenant { public Guid? TenantUid { get; set; } }
public sealed class TenantFlowEffectIntent : SchemataFlowEffectIntent, ITenant { public Guid? TenantUid { get; set; } }
```

Close the engine on those types instead of the defaults:

```csharp
schema.UseFlow<TenantProcess, TenantProcessToken, TenantProcessTransition,
               TenantProcessCompensation, TenantProcessParticipant, TenantProcessSource,
               TenantFlowEffectIntent>()
      .UseStateMachine()
      .Use<EnrollmentProcess>();
```

Each tenant container then needs the closed repository, its native source, and the closed resource
mutation for every row type:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Repository;
using Schemata.Tenancy.Skeleton.Entities;

void ConfigureFlowTenancy(SchemataTenancyBuilder<SchemataTenant> tenancy)
{
    tenancy.UseRepositories(tenant => {
        TenantRow<TenantProcess>(tenant);
        TenantRow<TenantProcessToken>(tenant);
        TenantRow<TenantProcessTransition>(tenant);
        TenantRow<TenantProcessParticipant>(tenant);
        TenantRow<TenantProcessSource>(tenant);
        TenantRow<TenantProcessCompensation>(tenant);
        TenantRow<TenantFlowEffectIntent>(tenant);
        tenant.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(tenantConnectionString)
            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
    });
}

static void TenantRow<TEntity>(TenantRepositoryBuilder<SchemataTenant> tenant)
    where TEntity : class
{
    tenant.Services.AddTransient<IRepository<TEntity>, EfCoreRepository<AppDbContext, TEntity>>();
    tenant.Services.AddScoped<IResourceMutation<TEntity>, ResourceMutation<TEntity>>();
}
```

`tenantConnectionString` is the tenant's connection; `TenantRepositoryBuilder<TTenant>.TenantUid`
selects it. `UseDatabase` in [Repository isolation](../documents/tenancy.md#database-per-tenant)
registers the repository and source together when each tenant owns a database; it does not require
`ITenant`, so it suits a tenancy host that keeps the shipped entity types unchanged.

Use your application's tenant type in place of `SchemataTenant` when configuring a custom catalog.
`UseRepositories` installs Tenancy-owned add, query, update, and remove advisors bound to the
verified tenant identity at container composition; the advisors apply to rows implementing
`ITenant`. A tenant row carries the subtype's nullable tenant identifier; null identifies a host
row. A host-registered repository resolves through the host binding, so it sees `TenantUid IS NULL`
rows only; tenant queries see only their tenant's rows.
Keep the identifier out of JSON and protobuf business models through the serializers' ignore
metadata. Parent and source associations continue to use canonical names.

A tenant that registers only the repository resolves the host open-generic mutation, which binds
the host repository and refuses to join a tenant unit of work. `UseFlow()` and the `ITenant` trait
alone do not activate repository isolation; the explicit per-tenant registration does. Omitting a
row type leaves that repository outside the tenant boundary, including direct child record reads
and writes. Tenant catalog and host records remain shared metadata and must not be registered as
tenant-owned repositories. Keep consumer name generation configured independently.

### Database uniqueness

The shipped entities declare unique indexes: process idempotency on
`(DefinitionName, IdempotencyKey)`, effect request IDs on `RequestId`, and source bindings on
`(Process, Name)` for `Token IS NULL` plus `(Process, Token, Name)` for `Token IS NOT NULL`. A
tenant subtype inherits them, and an inherited index that the subtype does not restate keeps its
host definition and still constrains tenant rows. Restate each property list with the tenant filter
and add the tenant-partition constraints; `SchemataModelCustomizer` replaces an inherited
declaration when the subtype declares the same property list.

EF Core applies the entities' generic `IndexAttribute.Filter` SQL through `HasFilter`. Filter SQL
uses the declared column names; consumers must adapt it when their provider or naming convention
requires different identifiers or lacks filtered indexes. LinqToDB `CreateTable` does not create
these indexes from the annotations. Consumers own their production schema DDL, including equivalent
host/tenant and process/token unique constraints. Framework registration supplies neither schema
migrations nor data migration tools.

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
            this.When<Student>(student => student.Age >= 18).Go(Approved),
            this.Otherwise().Go(Rejected));
    }
}

```

`Student` is the source entity already registered by Getting Started. The `When<T>` constraint is
`where T : class, ICanonicalName`; the predicate evaluates against the source bound to the current
token under the binding name `student` (the type name lowercased and underscored).

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

Registration also checks the definition against the engine's declared `FlowRuntimeCapabilities`.
The state-machine engine declares `ProcedureTasks` only, so a definition using parallel or
inclusive gateways, subprocesses, compensation, loops, or non-interrupting boundary events throws
`InvalidOperationException` at startup naming the missing capability. Those shapes need the full
BPMN engine — add `Schemata.Flow.Bpmn` and call `UseBpmn()` instead of `UseStateMachine()`.

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
  "source": "students/a1b2c3d4e5f6a7b8",
  "displayName": "...",
  "description": "...",
  "requestId": "..."
}
```

When `source` is present, the handler resolves the entity through `IRepository<T>` and binds it via
`IFlowRunner.StartAsync<TState>`. Use the `name` returned by the Student create endpoint. When
`source` is absent, the handler falls through to the sourceless overload. The endpoint responds
`200 OK` with the process row.

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

// Variant binding the Student source entity from Getting Started.
var process = await runner.StartAsync("EnrollmentProcess", student, ct: ct);
```

Both overloads accept an optional `StartProcessOptions` (`DisplayName`, `Description`,
`IdempotencyKey`) and return the persisted `SchemataProcess` row. The principal enters only
through the resource-method handlers; the programmatic interface carries none.

`IdempotencyKey` is distinct from `RequestId`: starting a process while another instance of the
same definition carries the same key in a non-terminal state throws `AlreadyExistsException`,
protecting a retried start from creating a duplicate. The key is released when the process
reaches a terminal state — it moves into the row's `schemata/flow/idempotency-key` annotation
and the pair can be reused for a new instance. The HTTP start verb accepts the same field as
`idempotency_key` on `StartProcessInstanceRequest`.

## Next steps

- [Event Bus](event-bus.md) — bridge BPMN `Message` and `Signal` catches to the event bus
- [Scheduling](scheduling.md) — fire timer catches through the scheduler

## See also

- [Flow Overview](../documents/flow/overview.md) — architecture, the BPMN subset, startup
- [Flow DSL](../documents/flow/dsl.md) — the full code-first builder
- [Flow HTTP Transport](../documents/flow/http.md) — the process verbs and read endpoints
