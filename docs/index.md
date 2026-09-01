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

| Layer             | Packages                                                                              | Purpose                                                              |
| ----------------- | ------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| **Abstractions**  | `Schemata.Abstractions`, `Schemata.Common`                                            | Entity traits, advisor interfaces, error types, resource attributes  |
| **Core**          | `Schemata.Core`, `Schemata.Advice`                                                    | Feature system, advice pipeline runner, builder API                  |
| **Repository**    | `Schemata.Entity.Repository`, `.EntityFrameworkCore`, `.LinqToDB`, `.Cache`, `.Owner` | Repository pattern, ORM integration, query caching, entity ownership |
| **Validation**    | `Schemata.Validation.Skeleton`, `.FluentValidation`                                   | Validation advisor pipeline with FluentValidation                    |
| **Mapping**       | `Schemata.Mapping.Skeleton`, `.Foundation`, `.AutoMapper`, `.Mapster`                 | Object-to-object mapping abstraction with pluggable backends         |
| **Security**      | `Schemata.Security.Skeleton`, `.Foundation`                                           | Access control and entitlement-based query filtering                 |
| **Identity**      | `Schemata.Identity.Skeleton`, `.Foundation`                                           | ASP.NET Core Identity integration with advisor-based registration    |
| **Authorization** | `Schemata.Authorization.Skeleton`, `.Foundation`, `.Identity`                         | OAuth 2.0 / OpenID Connect authorization server                      |
| **Tenancy**       | `Schemata.Tenancy.Skeleton`, `.Foundation`                                            | Multi-tenant resolution with per-tenant DI isolation                 |
| **Event**         | `Schemata.Event.Skeleton`, `.Foundation`, `.RabbitMQ`                                 | Event bus with in-process and RabbitMQ transports                    |
| **Actor**         | `Schemata.Actor.Skeleton`, `.Foundation`, `.Event`, `.Scheduling`, `Schemata.Flow.Actor`, `Schemata.Push.Actor`, `Schemata.Report.Actor` | In-process actor system with event, reminder, and per-module (Flow, Push, Report) serialization bridges |
| **Scheduling**    | `Schemata.Scheduling.Skeleton`, `.Foundation`, `.Event`, `.Http`, `.Grpc` | Cron, periodic, and one-time jobs with lifecycle event publishing and HTTP/gRPC bridges |
| **Resource**      | `Schemata.Resource.Foundation`, `.Http`, `.Grpc`                                      | Auto-generated CRUD endpoints (HTTP REST and gRPC)                   |
| **Flow**          | `Schemata.Flow.Foundation`, `.StateMachine`, `.Bpmn`, `.Http`, `.Grpc`, `.Scheduling`, `.Event`, `Schemata.Flow.Actor` | BPMN 2.0.2 process engine with state-machine and full BPMN runtimes plus HTTP/gRPC, scheduling, event, and actor bridges |
| **Modular**       | `Schemata.Modular`                                                                    | Module discovery and lifecycle management                            |
| **Modeling**      | `Schemata.Modeling.Generator`                                                         | SKM schema definition language for `.skm` files                      |

Many feature domains ship two packages: a **Skeleton** package (contracts and abstractions only) and a **Foundation** package (implementation). Both target `net8.0;net10.0`. Business libraries reference Skeleton packages; host applications reference Foundation packages.

## Feature Priority Table

Features are ordered by two independent integers. `Order` controls `ConfigureServices` sequence; `Priority` controls `ConfigureApplication` and `ConfigureEndpoints` sequence. The range `[100_000_000, 900_000_000]` is reserved for built-in features and Schemata extensions. User features pick numbers outside that range.

Several non-`10M` offsets are reserved above extension anchors. Flow engines use `+50K`
(`Flow.StateMachine`) and `+60K` (`Flow.Bpmn`). Domain-specific transport and bridge slots use
`+100K`, `+200K`, and `+300K`: `Scheduling.Event` occupies `+100K`, while `Flow.Event` occupies
`+300K`. Scheduling bridges use `+400K` (`Flow.Scheduling`, `Push.Scheduling`,
`Report.Scheduling`), and `+600K` is the actor bridge slot (`Flow.Actor`, `Push.Actor`,
`Report.Actor`). The `+5M` offset
is reserved separately for a built-in sub-feature; only `WellKnown` uses it.

### Built-in Features

| Priority    | Feature                | Description                                                  |
| ----------- | ---------------------- | ------------------------------------------------------------ |
| 100_000_000 | ForwardedHeaders       | ASP.NET Forwarded Headers middleware                         |
| 110_000_000 | DeveloperExceptionPage | ASP.NET Developer Exception Page middleware                  |
| 120_000_000 | Logging                | ASP.NET Request Logging middleware                           |
| 130_000_000 | HttpLogging            | ASP.NET HTTP Logging middleware                              |
| 140_000_000 | W3CLogging             | ASP.NET W3C Logging middleware                               |
| 150_000_000 | Https                  | ASP.NET HTTPS and HTTPS Redirection middlewares              |
| 160_000_000 | Tenancy                | Multi-tenant isolation middleware                            |
| 170_000_000 | CookiePolicy           | ASP.NET Cookie Policy middleware                             |
| 180_000_000 | Routing                | ASP.NET Routing middleware                                   |
| 185_000_000 | WellKnown              | Well-known endpoint sub-feature of Routing (+5M)             |
| 190_000_000 | Quota                  | ASP.NET Rate Limiter middleware                              |
| 200_000_000 | Cors                   | ASP.NET CORS middleware                                      |
| 210_000_000 | Authentication         | ASP.NET Authentication and Authorization middlewares         |
| 220_000_000 | Session                | ASP.NET Session middleware                                   |
| 230_000_000 | Controllers            | ASP.NET MVC middlewares, without Views                       |
| 240_000_000 | JsonSerializer         | System.Text.Json with snake_case and 53-bit integer handling |

### Extension Features

| Priority    | Package                           | Feature               | Description                                                            |
| ----------- | --------------------------------- | --------------------- | ---------------------------------------------------------------------- |
| 400_000_000 | Schemata.Security.Foundation      | Security              | RBAC/ABAC security policies                                            |
| 410_000_000 | Schemata.Transport.Http           | Transport.Http        | Shared HTTP plumbing: exception handler, JSON wire-name traits         |
| 420_000_000 | Schemata.Transport.Grpc           | Transport.Grpc        | Shared gRPC plumbing: code-first protobuf-net, interceptor, reflection |
| 430_000_000 | Schemata.Identity.Foundation      | Identity              | ASP.NET Core Identity integration                                      |
| 440_000_000 | Schemata.Event.Foundation         | Event                 | Event bus and dispatch pipeline                                        |
| 450_000_000 | Schemata.Actor.Foundation         | Actor                 | In-process actor system: per-instance mailbox serialization            |
| 450_100_000 | Schemata.Actor.Event              | Actor.Event           | Bridge: Actor + Event (+100K)                                          |
| 450_200_000 | Schemata.Actor.Scheduling         | Actor.Scheduling      | Bridge: Actor + Scheduling (+200K)                                     |
| 460_000_000 | Schemata.Authorization.Foundation | Authorization         | OAuth 2.0 / OpenID Connect server                                      |
| 460_100_000 | Schemata.Authorization.Identity   | AuthorizationIdentity | Bridge: Authorization + Identity (+100K)                               |
| 470_000_000 | Schemata.Mapping.Foundation       | Mapping               | Unified object mapper abstraction                                      |
| 480_000_000 | Schemata.Scheduling.Foundation    | Scheduling            | Cron and periodic job scheduler                                        |
| 480_100_000 | Schemata.Scheduling.Event         | Scheduling.Event      | Bridge: Scheduling + Event (+100K)                                     |
| 480_200_000 | Schemata.Scheduling.Http           | Scheduling.Http        | Bridge: Scheduling + HTTP transport (+200K)                            |
| 480_300_000 | Schemata.Scheduling.Grpc           | Scheduling.Grpc        | Bridge: Scheduling + gRPC transport (+300K)                            |
| 490_000_000 | Schemata.Flow.Foundation          | Flow                  | BPMN process engine                                                    |
| 490_050_000 | Schemata.Flow.StateMachine        | Flow.StateMachine     | Default state-machine engine on Flow (+50K)                            |
| 490_060_000 | Schemata.Flow.Bpmn                | Flow.Bpmn             | Full BPMN 2.0.2 engine on Flow (+60K)                                  |
| 490_100_000 | Schemata.Flow.Http                | Flow.Http             | Flow HTTP transport (+100K)                                            |
| 490_200_000 | Schemata.Flow.Grpc                | Flow.Grpc             | Flow gRPC transport (+200K)                                            |
| 490_300_000 | Schemata.Flow.Event               | Flow.Event            | Bridge: Flow + Event (+300K)                                           |
| 490_400_000 | Schemata.Flow.Scheduling          | Flow.Scheduling       | Bridge: Flow + Scheduling (+400K)                                      |
| 490_600_000 | Schemata.Flow.Actor               | Flow.Actor            | Bridge: Flow + Actor (+600K)                                           |
| 500_000_000 | Schemata.Resource.Foundation      | Resource              | Google AIP-compliant resource service                                  |
| 500_100_000 | Schemata.Resource.Http            | Resource.Http         | HTTP/REST endpoint generation (+100K)                                  |
| 500_200_000 | Schemata.Resource.Grpc            | Resource.Grpc         | gRPC endpoint generation (+200K)                                       |
| 510_000_000 | Schemata.Insight.Foundation       | Insight               | Federated query planning and execution over resource entities         |
| 510_100_000 | Schemata.Insight.Http             | Insight.Http          | Insight HTTP transport (+100K)                                         |
| 510_200_000 | Schemata.Insight.Grpc             | Insight.Grpc          | Insight gRPC transport (+200K)                                         |
| 520_000_000 | Schemata.Push.Foundation          | Push                  | Push notification fan-out                                              |
| 520_400_000 | Schemata.Push.Scheduling          | Push.Scheduling       | Bridge: Push + Scheduling (+400K)                                      |
| 520_600_000 | Schemata.Push.Actor               | Push.Actor            | Bridge: Push + Actor (+600K)                                          |
| 530_000_000 | Schemata.Report.Foundation        | Report                | Report definitions, snapshots, and generation                         |
| 530_100_000 | Schemata.Report.Http              | Report.Http           | Report HTTP transport (+100K)                                          |
| 530_200_000 | Schemata.Report.Grpc              | Report.Grpc           | Report gRPC transport (+200K)                                          |
| 530_400_000 | Schemata.Report.Scheduling        | Report.Scheduling     | Bridge: Report + Scheduling (+400K)                                    |
| 530_600_000 | Schemata.Report.Actor             | Report.Actor          | Bridge: Report + Actor (+600K)                                        |
| 540_000_000 | Schemata.Modular                  | Modular               | Module discovery and lifecycle management                                |

## Documentation

- **[Guides](guides/overview.md)** — step-by-step tutorials building a complete application from scratch
- **[Cookbook](cookbook/overview.md)** — scenario-driven, end-to-end recipes for advanced use cases
- **[Documents](documents/overview.md)** — technical reference for framework internals and all subsystems
- **[Modeling](modeling/overview.md)** — SKM language reference for entity code generation
- **API Reference** — generated from XML doc comments in `src/`; browse it through the **References** node in the site navigation.
