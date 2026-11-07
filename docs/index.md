# Schemata

Schemata is a modular .NET framework for building enterprise applications. It provides a layered architecture with pluggable features and an advisor-based extensibility pipeline. Its code-first resource APIs implement selected Google AIP requirements; coverage and gaps are recorded per requirement and transport.

## Capability boundaries

- Resource contracts: [modeling matrix](documents/resource/aip-modeling.md#requirement-matrix), [interaction matrix](documents/resource/aip-interactions.md), and [business-logic matrix](documents/resource/aip-business-logic.md).
- OAuth/OpenID Connect: [protocol requirement matrix](documents/authorization.md#protocol-requirement-matrix), opt-in flows, historical downgrades, and a separate [browser evidence matrix](documents/authorization.md#browser-and-external-rp-evidence-matrix).
- Flow: [BPMN executable-subset matrix](documents/flow/bpmn-engine.md#feature-matrix), including runtime limits and unsupported notation.

These references distinguish source-traced behavior, recorded executions, and application responsibilities. They make neither whole-standard compliance nor certification claims.

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

The tracked tree contains 90 source package projects, 10 target projects, two generator projects,
and 68 test projects. The package families below summarize the source and generator inventories.

| Layer             | Packages                                                                              | Purpose                                                              |
| ----------------- | ------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| **Abstractions**  | `Schemata.Abstractions`, `Schemata.Common`                                            | Entity traits, advisor interfaces, error types, resource attributes  |
| **Core**          | `Schemata.Core`, `Schemata.Advice`                                                    | Feature system, advice pipeline runner, builder API                  |
| **Repository** | `Schemata.Entity.Repository`, `.EntityFrameworkCore`, `.LinqToDB`, `.Cache`, `.Event` | Repository pattern, ORM integration, query caching, committed events |
| **Validation**    | `Schemata.Validation.Skeleton`, `.FluentValidation`                                   | Validation advisor pipeline with FluentValidation                    |
| **Mapping**       | `Schemata.Mapping.Skeleton`, `.Foundation`, `.AutoMapper`, `.Mapster`                 | Object-to-object mapping abstraction with pluggable backends         |
| **Security** | `Schemata.Security.Skeleton`, `.Foundation`, `.Grpc` | Access policy, entitlement filtering, gRPC signature-message helpers |
| **Identity** | `Schemata.Identity.Skeleton`, `.Foundation`, `.Scim` | Identity managers, account APIs, external provisioning |
| **Authorization** | `Schemata.Authorization.Skeleton`, `.Foundation`, `.Identity`, `.Scim` | OAuth/OIDC server, Identity bridge, Application provisioning |
| **Tenancy** | `Schemata.Tenancy.Skeleton`, `.Foundation`, `.Actor`, `.Caching`, `.Messaging` | Tenant resolution and capability adapters |
| **Event** | `Schemata.Event.Skeleton`, `.Foundation`, `.RabbitMq` | Event bus and broker adapter |
| **Caching** | `Schemata.Caching.Skeleton`, `.Memory`, `.Redis` | Cache contracts and backends |
| **Messaging** | `Schemata.Messaging.Skeleton`, `.RabbitMq` | Dispatch contracts and broker adapter |
| **Transport** | `Schemata.Transport.Http`, `.Grpc`, `.RabbitMq` | Shared transport infrastructure |
| **SCIM** | `Schemata.Scim` | Shared provisioning protocol |
| **Domain** | `Schemata.Domain.Skeleton` | Aggregate and domain-event contracts |
| **Expressions** | `Schemata.Expressions.Skeleton`, `.Aip`, `.Cel`, `.Order` | Expression contracts, filtering languages, and ordering |
| **Actor**         | `Schemata.Actor.Skeleton`, `.Foundation`, `.Event`, `.Scheduling`, `Schemata.Flow.Actor`, `Schemata.Push.Actor`, `Schemata.Report.Actor` | In-process actor system with event, reminder, and per-module (Flow, Push, Report) serialization bridges |
| **Scheduling**    | `Schemata.Scheduling.Skeleton`, `.Foundation`, `.Event`, `.Http`, `.Grpc` | Cron, periodic, and one-time jobs with lifecycle event publishing and HTTP/gRPC bridges |
| **Quota**         | `Schemata.Quota.Skeleton`, `.Foundation`, `.Http`, `.Grpc`                     | Durable weighted reservations, settlement, ledger, expiry, and authenticated transports |
| **Resource**      | `Schemata.Resource.Foundation`, `.Http`, `.Grpc`                                      | Auto-generated CRUD endpoints (HTTP REST and gRPC)                   |
| **Flow** | `Schemata.Flow.Skeleton`, `.Foundation`, `.StateMachine`, `.Bpmn`, `.Http`, `.Grpc`, `.Scheduling`, `.Event`, `.Actor` | Process contracts, runtimes, transports, and bridges |
| **Insight** | `Schemata.Insight.Skeleton`, `.Foundation`, `.Http`, `.Grpc` | Query contracts, execution, and transports |
| **Push** | `Schemata.Push.Skeleton`, `.Foundation`, `.Http`, `.Grpc`, `.Scheduling`, `.Actor`, `.WebPush` | Notification control and delivery |
| **Report** | `Schemata.Report.Skeleton`, `.Foundation`, `.Http`, `.Grpc`, `.Scheduling`, `.Actor` | Definitions, generation, and snapshots |
| **Modular**       | `Schemata.Modular`                                                                    | Module discovery and lifecycle management                            |
| **Advice generator** | `Schemata.Advice.Generator` | Advisor extension and runner generation |
| **Modeling**      | `Schemata.Modeling.Generator`                                                         | SKM schema definition language for `.skm` files                      |

Many feature domains ship two packages: a **Skeleton** package (contracts and abstractions only) and a **Foundation** package (implementation). Both target `net8.0;net10.0`. Business libraries reference Skeleton packages; host applications reference Foundation packages.

## Feature priorities

The [built-in and extension feature reference](documents/core/built-in-features.md)
contains the authoritative priority table, ordering rules, and activation details.

## Documentation

- **[Guides](guides/overview.md)** — step-by-step tutorials building a complete application from scratch
- **[Cookbook](cookbook/overview.md)** — scenario-driven, end-to-end recipes for advanced use cases
- **[Documents](documents/overview.md)** — technical reference for framework internals and all subsystems
- **[Modeling](modeling/overview.md)** — SKM language reference for entity code generation
- **API Reference** — generated from XML doc comments in `src/`; browse it through the **References** node in the site navigation.
