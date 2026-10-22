# Schemata

Schemata is a modular .NET framework for building enterprise applications. It provides a layered architecture with pluggable features, an advisor-based extensibility pipeline, and conventions aligned with [Google API Improvement Proposals](https://google.aip.dev/general).

## Design Philosophy

**Trait-based entity modeling.** Entities declare capabilities through marker interfaces (`ITimestamp`, `ISoftDelete`, `IConcurrency`, etc.). Built-in advisors registered alongside the repository pipeline check each entity against the matching trait with plain `is`-checks inside their `AdviseAsync` methods, then apply timestamp tracking, soft-delete filtering, concurrency checks, and so on. Custom trait behavior is added by registering an additional advisor that performs the same kind of check.

**Advisor pipelines for cross-cutting concerns.** Every operation (repository CRUD, HTTP resource handling, user registration, flow transitions) passes through an ordered pipeline of advisors. Each advisor can inspect, modify, or short-circuit the operation. Built-in advisors handle validation, authorization, caching, idempotency, and freshness. Custom advisors plug in alongside them through standard DI registration.

**Feature-based composition.** The application is assembled from independent features, each registering its own services, middleware, and endpoints. Features declare ordering and dependencies, so adding or removing capabilities requires no changes to the rest of the application.

## Quick Start

```shell
dotnet new web
dotnet add package --prerelease Schemata.Application.Complex.Targets
```

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseJsonSerializer();
        schema.UseResource().MapHttp().Use<Student>();
    });

var app = builder.Build();
app.Run();
```

See [Getting Started](guides/getting-started.md) for a complete walkthrough building a Student CRUD API.

## Package Layers

The `src/` tree contains **88** runtime package projects; the two generator projects under
`generators/` and the 10 target meta-packages under `targets/` are separate (the modeling generator
is listed below for completeness). Packages follow the
`Schemata.<Domain>.<Layer>` convention: `Skeleton` carries contracts, `Foundation` carries the
implementation and its `Use{Domain}` registration, `Http`/`Grpc` are transports, and named
provider/bridge packages connect a component to a transport, broker, storage backend, or neighboring
runtime.

| Domain             | Packages |
| ------------------ | -------- |
| **Abstractions**   | `Schemata.Abstractions`, `Schemata.Common`, `Schemata.Domain.Skeleton`, `Schemata.Messaging.Skeleton` |
| **Kernel**         | `Schemata.Core`, `Schemata.Advice`, `Schemata.Modular` |
| **Repository**     | `Schemata.Entity.Repository`, `Schemata.Entity.EntityFrameworkCore`, `Schemata.Entity.LinqToDB`, `Schemata.Entity.Cache`, `Schemata.Entity.Event` |
| **Validation**     | `Schemata.Validation.Skeleton`, `Schemata.Validation.FluentValidation` |
| **Mapping**        | `Schemata.Mapping.Skeleton`, `Schemata.Mapping.Foundation`, `Schemata.Mapping.AutoMapper`, `Schemata.Mapping.Mapster` |
| **Security**       | `Schemata.Security.Skeleton`, `Schemata.Security.Foundation` |
| **Identity**       | `Schemata.Identity.Skeleton`, `Schemata.Identity.Foundation`, `Schemata.Identity.Http`, `Schemata.Identity.Grpc` |
| **SCIM**           | `Schemata.Scim`, `Schemata.Identity.Scim` |
| **Authorization**  | `Schemata.Authorization.Skeleton`, `Schemata.Authorization.Foundation`, `Schemata.Authorization.Http`, `Schemata.Authorization.Grpc`, `Schemata.Authorization.Identity` |
| **Tenancy**        | `Schemata.Tenancy.Skeleton`, `Schemata.Tenancy.Foundation`, `Schemata.Tenancy.Actor`, `Schemata.Tenancy.Caching`, `Schemata.Tenancy.Messaging` |
| **Event**          | `Schemata.Event.Skeleton`, `Schemata.Event.Foundation`, `Schemata.Event.RabbitMq` |
| **Actor**          | `Schemata.Actor.Skeleton`, `Schemata.Actor.Foundation`, `Schemata.Actor.Event`, `Schemata.Actor.Scheduling`; the Flow/Push/Report actor bridges ship in their own domains |
| **Transport**      | `Schemata.Transport.Http`, `Schemata.Transport.Grpc`, `Schemata.Transport.RabbitMq` |
| **Scheduling**     | `Schemata.Scheduling.Skeleton`, `Schemata.Scheduling.Foundation`, `Schemata.Scheduling.Event`, `Schemata.Scheduling.Http`, `Schemata.Scheduling.Grpc` |
| **Flow**           | `Schemata.Flow.Skeleton`, `Schemata.Flow.Foundation`, `Schemata.Flow.StateMachine`, `Schemata.Flow.Bpmn`, `Schemata.Flow.Http`, `Schemata.Flow.Grpc`, `Schemata.Flow.Event`, `Schemata.Flow.Scheduling`, `Schemata.Flow.Actor` |
| **Resource**       | `Schemata.Resource.Foundation`, `Schemata.Resource.Http`, `Schemata.Resource.Grpc` |
| **Insight**        | `Schemata.Insight.Skeleton`, `Schemata.Insight.Foundation`, `Schemata.Insight.Http`, `Schemata.Insight.Grpc` |
| **Push**           | `Schemata.Push.Skeleton`, `Schemata.Push.Foundation`, `Schemata.Push.Http`, `Schemata.Push.Grpc`, `Schemata.Push.Scheduling`, `Schemata.Push.Actor`, `Schemata.Push.WebPush` |
| **Report**         | `Schemata.Report.Skeleton`, `Schemata.Report.Foundation`, `Schemata.Report.Http`, `Schemata.Report.Grpc`, `Schemata.Report.Scheduling`, `Schemata.Report.Actor` |
| **Caching**        | `Schemata.Caching.Skeleton`, `Schemata.Caching.Memory`, `Schemata.Caching.Redis` |
| **Expressions**    | `Schemata.Expressions.Skeleton`, `Schemata.Expressions.Aip`, `Schemata.Expressions.Cel`, `Schemata.Expressions.Order` |
| **Modeling**       | `Schemata.Modeling.Generator` |

The repository adapters of the Flow and Scheduling foundations live inside
`Schemata.Flow.Foundation/Repository` and `Schemata.Scheduling.Foundation/Repository`; there are no
separate `*.Repository` packages. Business libraries reference Skeleton packages; host applications
reference Foundation packages.

## Feature Priority Table

Features are ordered by two independent integers. `Order` controls the `ConfigureServices` sequence;
`Priority` controls the `ConfigureApplication` and `ConfigureEndpoints` sequence. The range
`[100_000_000, 900_000_000]` is reserved for built-in features and Schemata extensions. User
features pick numbers outside that range.

Several non-`10M` offsets are reserved above extension anchors. Flow engines use `+50K`
(`Flow.StateMachine`) and `+60K` (`Flow.Bpmn`). Domain-specific transport and bridge slots use
`+100K`, `+200K`, and `+300K`: `Scheduling.Event` occupies `+100K`, while `Flow.Event` occupies
`+300K`. Scheduling bridges use `+400K` (`Flow.Scheduling`, `Push.Scheduling`,
`Report.Scheduling`), and `+600K` is the actor bridge slot (`Flow.Actor`, `Push.Actor`,
`Report.Actor`). The `+5M` offset is reserved for a built-in sub-feature; only `WellKnown` uses it.

The single canonical table — every built-in and extension feature class, its exact `Priority`, its
`Use*()` activation, and what it registers — is
[Built-in Features](documents/core/built-in-features.md). It is generated from the feature constants
at HEAD and is the only table to update when a priority changes; this page and the README link to it
instead of duplicating it.

## Documentation

- **[Guides](guides/overview.md)** — step-by-step tutorials building a complete application from scratch
- **[Cookbook](cookbook/overview.md)** — scenario-driven, end-to-end recipes for advanced use cases
- **[Documents](documents/overview.md)** — technical reference for framework internals and all subsystems
- **[Modeling](modeling/overview.md)** — SKM language reference for entity code generation
- **API Reference** — generated from XML doc comments in `src/`; browse it through the **References** node in the site navigation.
