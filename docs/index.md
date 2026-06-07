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

Packages are organized in layers. Higher layers depend on lower ones; consumers reference the tier that matches their needs.

| Layer | Packages | Purpose |
| --- | --- | --- |
| **Abstractions** | `Schemata.Abstractions`, `Schemata.Common` | Entity traits, advisor interfaces, error types, resource attributes |
| **Core** | `Schemata.Core`, `Schemata.Advice` | Feature system, advice pipeline runner, builder API |
| **Repository** | `Schemata.Entity.Repository`, `.EntityFrameworkCore`, `.LinqToDB`, `.Cache`, `.Owner` | Repository pattern, ORM integration, query caching, entity ownership |
| **Validation** | `Schemata.Validation.Skeleton`, `.FluentValidation` | Validation advisor pipeline with FluentValidation |
| **Mapping** | `Schemata.Mapping.Skeleton`, `.Foundation`, `.AutoMapper`, `.Mapster` | Object-to-object mapping abstraction with pluggable backends |
| **Security** | `Schemata.Security.Skeleton`, `.Foundation` | Access control and entitlement-based query filtering |
| **Identity** | `Schemata.Identity.Skeleton`, `.Foundation` | ASP.NET Core Identity integration with advisor-based registration |
| **Authorization** | `Schemata.Authorization.Skeleton`, `.Foundation`, `.Identity` | OAuth 2.0 / OpenID Connect authorization server |
| **Tenancy** | `Schemata.Tenancy.Skeleton`, `.Foundation` | Multi-tenant resolution with per-tenant DI isolation |
| **Event** | `Schemata.Event.Skeleton`, `.Foundation`, `.RabbitMQ` | Event bus with in-process and RabbitMQ transports |
| **Scheduling** | `Schemata.Scheduling.Skeleton`, `.Foundation` | Cron and periodic job scheduling with lifecycle observers |
| **Resource** | `Schemata.Resource.Foundation`, `.Http`, `.Grpc` | Auto-generated CRUD endpoints (HTTP REST and gRPC) |
| **Flow** | `Schemata.Flow.Foundation`, `.Http`, `.Grpc`, `.Scheduling`, `.Event` | BPMN 2.0.2 process engine with transport and integration bridges |
| **Modular** | `Schemata.Modular` | Module discovery and lifecycle management |
| **Modeling** | `Schemata.Modeling.Generator` | SKM schema definition language for `.skm` files |

Many feature domains ship two packages: a **Skeleton** package (contracts and abstractions only) and a **Foundation** package (implementation). Both target `net8.0;net10.0`. Business libraries reference Skeleton packages; host applications reference Foundation packages.

## Feature Priority Table

Features are ordered by two independent integers. `Order` controls `ConfigureServices` sequence; `Priority` controls `ConfigureApplication` and `ConfigureEndpoints` sequence. The range `[100_000_000, 900_000_000]` is reserved for built-in features and Schemata extensions. User features pick numbers outside that range.

Two non-`10M` offsets are also reserved: `+5M` for a sub-feature of a built-in (only `WellKnown` uses this today), and `+100K` / `+200K` for bridges that wire two extension features together. When two bridges share the same later-feature anchor (e.g. `Flow.Event` and `Flow.Scheduling` both sit above `Flow`), they stack at `+100K` and `+200K` respectively.

### Built-in Features

| Priority | Feature | Description |
| --- | --- | --- |
| 100_000_000 | ForwardedHeaders | ASP.NET Forwarded Headers middleware |
| 110_000_000 | DeveloperExceptionPage | ASP.NET Developer Exception Page middleware |
| 120_000_000 | Logging | ASP.NET Request Logging middleware |
| 130_000_000 | HttpLogging | ASP.NET HTTP Logging middleware |
| 140_000_000 | W3CLogging | ASP.NET W3C Logging middleware |
| 150_000_000 | Https | ASP.NET HTTPS and HTTPS Redirection middlewares |
| 160_000_000 | Tenancy | Multi-tenant isolation middleware |
| 170_000_000 | CookiePolicy | ASP.NET Cookie Policy middleware |
| 180_000_000 | Routing | ASP.NET Routing middleware |
| 185_000_000 | WellKnown | Well-known endpoint sub-feature of Routing (+5M) |
| 190_000_000 | Quota | ASP.NET Rate Limiter middleware |
| 200_000_000 | Cors | ASP.NET CORS middleware |
| 210_000_000 | Authentication | ASP.NET Authentication and Authorization middlewares |
| 220_000_000 | Session | ASP.NET Session middleware |
| 230_000_000 | Controllers | ASP.NET MVC middlewares, without Views |
| 240_000_000 | JsonSerializer | System.Text.Json with snake_case and 53-bit integer handling |

### Extension Features

| Priority | Package | Feature | Description |
| --- | --- | --- | --- |
| 400_000_000 | Schemata.Security.Foundation | Security | RBAC/ABAC security policies |
| 410_000_000 | Schemata.Transport.Http | Transport.Http | Shared HTTP plumbing: exception handler, JSON wire-name traits |
| 420_000_000 | Schemata.Transport.Grpc | Transport.Grpc | Shared gRPC plumbing: code-first protobuf-net, interceptor, reflection |
| 430_000_000 | Schemata.Identity.Foundation | Identity | ASP.NET Core Identity integration |
| 440_000_000 | Schemata.Event.Foundation | Event | Event bus and dispatch pipeline |
| 450_000_000 | Schemata.Authorization.Foundation | Authorization | OAuth 2.0 / OpenID Connect server |
| 450_100_000 | Schemata.Authorization.Identity | AuthorizationIdentity | Bridge: Authorization + Identity (+100K) |
| 460_000_000 | Schemata.Mapping.Foundation | Mapping | Unified object mapper abstraction |
| 470_000_000 | Schemata.Scheduling.Foundation | Scheduling | Cron and periodic job scheduler |
| 470_100_000 | Schemata.Scheduling.Event | Scheduling.Event | Bridge: Scheduling + Event (+100K) |
| 480_000_000 | Schemata.Flow.Foundation | Flow | BPMN process engine |
| 480_100_000 | Schemata.Flow.Http | Flow.Http | Flow HTTP transport (+100K) |
| 480_200_000 | Schemata.Flow.Grpc | Flow.Grpc | Flow gRPC transport (+200K) |
| 480_300_000 | Schemata.Flow.Event | Flow.Event | Bridge: Flow + Event (+300K) |
| 480_400_000 | Schemata.Flow.Scheduling | Flow.Scheduling | Bridge: Flow + Scheduling (+400K) |
| 490_000_000 | Schemata.Resource.Foundation | Resource | Google AIP-compliant resource service |
| 490_100_000 | Schemata.Resource.Http | Resource.Http | HTTP/REST endpoint generation (+100K) |
| 490_200_000 | Schemata.Resource.Grpc | Resource.Grpc | gRPC endpoint generation (+200K) |
| 520_000_000 | Schemata.Modular | Modular | Module discovery and loading |

## Documentation

- **[Guides](guides/overview.md)** — step-by-step tutorials building a complete application from scratch
- **[Cookbook](cookbook/overview.md)** — scenario-driven, end-to-end recipes for advanced use cases
- **[Documents](documents/overview.md)** — technical reference for framework internals and all subsystems
- **[Modeling](modeling/overview.md)** — SKM language reference for entity code generation
- **API Reference** — generated from XML doc comments in `src/`; browse it through the **References** node in the site navigation.
