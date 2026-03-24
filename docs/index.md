# Schemata

Schemata is a modular .NET framework for building enterprise applications. It provides a layered architecture with pluggable features, an advisor-based extensibility pipeline, and conventions aligned with [Google API Improvement Proposals](https://google.aip.dev/general).

## Design Philosophy

Schemata is built around three core ideas:

**Trait-based entity modeling.** Entities declare capabilities through small marker interfaces (`ITimestamp`, `ISoftDelete`, `IConcurrency`, etc.). The framework detects these traits at runtime and automatically applies the corresponding behavior -- timestamp tracking, soft-delete filtering, concurrency checks -- without any explicit wiring.

**Advisor pipelines for cross-cutting concerns.** Every operation (repository CRUD, HTTP resource handling, user registration, workflow transitions) passes through an ordered pipeline of advisors. Each advisor can inspect, modify, or short-circuit the operation. Built-in advisors handle validation, authorization, caching, idempotency, and freshness. Custom advisors plug in alongside them through standard DI registration.

**Feature-based composition.** The application is assembled from independent features, each registering its own services, middleware, and endpoints. Features declare ordering and dependencies, making it straightforward to add or remove capabilities without touching the rest of the application.

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
    });

var app = builder.Build();
app.Run();
```

See the [Getting Started](guides/getting-started.md) guide for a complete walkthrough building a CRUD API.

## Package Layers

Packages are organized in three layers. Higher layers depend on lower ones; consumers only need to reference the layer that matches their target framework.

| Layer             | Packages                                                                    | Purpose                                                             |
| ----------------- | --------------------------------------------------------------------------- | ------------------------------------------------------------------- |
| **Abstractions**  | `Schemata.Abstractions`                                                     | Entity traits, advisor interfaces, error types, resource attributes |
| **Core**          | `Schemata.Core`, `Schemata.Advice`, `Schemata.Common`                       | Feature system, advisor pipeline runner, builder API                |
| **Data**          | `Schemata.Entity.Repository`, `.EntityFrameworkCore`, `.LinqToDB`, `.Cache` | Repository pattern, ORM integration, query caching                  |
| **Validation**    | `Schemata.Validation.Skeleton`, `.FluentValidation`                         | Validation advisor pipeline with FluentValidation                   |
| **Mapping**       | `Schemata.Mapping.Skeleton`, `.AutoMapper`, `.Mapster`                      | Object-to-object mapping abstraction                                |
| **Security**      | `Schemata.Security.Skeleton`, `.Foundation`                                 | Access control and entitlement-based query filtering                |
| **Identity**      | `Schemata.Identity.Skeleton`, `.Foundation`                                 | ASP.NET Core Identity integration with advisor-based registration   |
| **Authorization** | `Schemata.Authorization.Skeleton`, `.Foundation`                            | OAuth 2.0 / OpenID Connect                                          |
| **Tenancy**       | `Schemata.Tenancy.Skeleton`, `.Foundation`                                  | Multi-tenant resolution with per-tenant DI isolation                |
| **Resource**      | `Schemata.Resource.Foundation`, `.Http`, `.Grpc`                            | Auto-generated CRUD endpoints (HTTP REST and gRPC)                  |
| **Workflow**      | `Schemata.Workflow.Skeleton`, `.Foundation`                                 | State machine orchestration with Automatonymous                     |
| **Modular**       | `Schemata.Modular`                                                          | Module discovery and lifecycle management                           |
| **Modeling**      | `Schemata.Modeling.Generator`                                               | SKM schema definition language for `.skm` files                     |

Many feature domains ship two packages: a **Skeleton** package (contracts and abstractions only, targeting `netstandard2.0`+) and a **Foundation** package (full implementation, targeting `net8.0`+). Business libraries that should remain portable reference Skeleton packages. Host applications reference Foundation packages.

## Documentation

- **[Guides](guides/overview.md)** -- Step-by-step tutorials building a complete application from scratch
- **[Documents](documents/overview.md)** -- Technical reference for framework internals and all subsystems
- **[Modeling](modeling/overview.md)** -- SKM language reference for schema definition
