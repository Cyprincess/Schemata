# Schemata

A .NET application framework for building modular, extensible business applications.

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cyprincess/Schemata/build.yml)](https://github.com/Cyprincess/Schemata/actions/workflows/build.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=Schemata&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Schemata)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=Schemata&metric=coverage)](https://sonarcloud.io/summary/new_code?id=Schemata)
[![license](https://img.shields.io/github/license/Cyprincess/Schemata.svg)](https://github.com/Cyprincess/Schemata/blob/master/LICENSE)
![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg)
![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg)

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

Add more capabilities from the [Feature Domains](#feature-domains) below.

## Documentation

- **[Guides](docs/guides/overview.md)** — step-by-step tutorials building a complete application from scratch
- **[Documents](docs/documents/overview.md)** — technical reference for framework internals and all subsystems
- **[Modeling](docs/modeling/overview.md)** — SKM language reference for entity code generation

## Feature Domains

- [Authorization](https://nuget.org/packages/Schemata.Authorization.Foundation) — OAuth 2.0 / OpenID Connect server
- [Caching](https://nuget.org/packages/Schemata.Caching.Skeleton) — distributed cache abstraction; Redis and `IDistributedCache` adapters
- [DSL](https://nuget.org/packages/Schemata.Modeling.Generator) — `.skm` source generator
- [Event](https://nuget.org/packages/Schemata.Event.Foundation) — in-process / RabbitMQ event bus
- [Flow](https://nuget.org/packages/Schemata.Flow.Foundation) — BPMN process engine, HTTP/gRPC transports, event/scheduling bridges
- [Identity](https://nuget.org/packages/Schemata.Identity.Foundation) — ASP.NET Core Identity integration
- [Mapping](https://nuget.org/packages/Schemata.Mapping.Foundation) — unified object-mapper abstraction (AutoMapper / Mapster)
- [Modular](https://nuget.org/packages/Schemata.Module.Complex.Targets) — module discovery and loading
- [Repository](https://nuget.org/packages/Schemata.Entity.Repository) — EF Core / LinqToDB providers with advisor pipeline, unit of work, ownership, query caching
- [Resource](https://nuget.org/packages/Schemata.Resource.Foundation) — Google AIP-compliant CRUD service over HTTP and gRPC
- [Scheduling](https://nuget.org/packages/Schemata.Scheduling.Foundation) — persistent cron / periodic / one-time job scheduler
- [Tenancy](https://nuget.org/packages/Schemata.Tenancy.Foundation) — multi-tenant resolution and per-tenant DI
- [Validation](https://nuget.org/packages/Schemata.Validation.FluentValidation) — FluentValidation integration

Scheduling packages are intentionally not bundled into any meta target package, like `Schemata.Flow.Bpmn`. Consumers add the required scheduling package explicitly with `<PackageReference />`.

## Features

Features are modular components that can be integrated at startup.

Features are characterized by `Order` and `Priority`, both of which are `Int32` values. `Order` controls the sequence of `ConfigureServices` calls; `Priority` controls the sequence of `ConfigureApplication` and `ConfigureEndpoints` calls.

The range `[100_000_000, 900_000_000]` for `Order` and `Priority` is reserved for built-in features and Schemata extensions.

### Built-in Features

A built-in feature can be activated by calling the `UseXXX` method on the `SchemataBuilder` instance. These features may also have additional configuration methods.

| Priority    | Feature                | Description                                                                          |
| ----------- | ---------------------- | ------------------------------------------------------------------------------------ |
| 100_000_000 | ForwardedHeaders       | ASP.NET Forwarded Headers Middleware                                                 |
| 110_000_000 | DeveloperExceptionPage | ASP.NET Developer Exception Page Middleware                                          |
| 120_000_000 | Logging                | ASP.NET Logging Middleware                                                           |
| 130_000_000 | HttpLogging            | ASP.NET HTTP Logging Middleware                                                      |
| 140_000_000 | W3CLogging             | ASP.NET W3C Logging Middleware                                                       |
| 150_000_000 | Https                  | ASP.NET HTTPS & HTTPS Redirection Middlewares                                        |
| 160_000_000 | Tenancy                | Multi-tenant isolation middleware (Order: 900_000_000)                               |
| 170_000_000 | CookiePolicy           | ASP.NET Cookie Policy Middleware                                                     |
| 180_000_000 | Routing                | ASP.NET Routing Middleware                                                           |
| 185_000_000 | WellKnown              | `/.well-known/*` routes (+5M sub-feature of Routing)                                 |
| 190_000_000 | Quota                  | ASP.NET Rate Limiter Middleware                                                      |
| 200_000_000 | Cors                   | ASP.NET CORS Middleware                                                              |
| 210_000_000 | Authentication         | ASP.NET Authentication & Authorization Middlewares                                   |
| 220_000_000 | Session                | ASP.NET Session Middleware                                                           |
| 230_000_000 | Controllers            | ASP.NET MVC Middlewares, without Views                                               |
| 240_000_000 | JsonSerializer         | Configure System.Text.Json to use snake_case and handle JavaScript's 53-bit integers |

### Extension Features

An extension feature can be activated in the same way as a built-in feature.

| Priority    | Package                            | Feature                  | Description                                                        |
| ----------- | ---------------------------------- | ------------------------ | ------------------------------------------------------------------ |
| 400_000_000 | Schemata.Security.Foundation       | Security                 | RBAC/ABAC security policies                                        |
| 410_000_000 | Schemata.Transport.Http            | Transport.Http           | Shared HTTP plumbing: exception handler, JSON wire-name traits     |
| 420_000_000 | Schemata.Transport.Grpc            | Transport.Grpc           | Shared gRPC plumbing: `AddCodeFirstGrpc`, interceptor, reflection  |
| 430_000_000 | Schemata.Identity.Foundation       | Identity                 | ASP.NET Core Identity integration                                  |
| 440_000_000 | Schemata.Event.Foundation          | Event                    | Pub/sub bus, type registry, publish/consume advisor pipeline       |
| 450_000_000 | Schemata.Authorization.Foundation  | Authorization            | OAuth 2.0 / OpenID Connect server                                  |
| 460_000_000 | Schemata.Mapping.Foundation        | Mapping                  | Unified object mapper abstraction                                  |
| 470_000_000 | Schemata.Scheduling.Foundation     | Scheduling               | Persistent cron / periodic / one-time job scheduler                |
| 470_100_000 | Schemata.Scheduling.Event          | Scheduling.Event         | Lifecycle event publisher bridging the scheduler to the event bus  |
| 470_200_000 | Schemata.Scheduling.Http           | Scheduling.Http          | HTTP resource bridge for jobs and long-running operations          |
| 470_300_000 | Schemata.Scheduling.Grpc           | Scheduling.Grpc          | gRPC resource bridge for jobs and long-running operations          |
| 480_000_000 | Schemata.Flow.Foundation           | Flow                     | BPMN process engine and state-machine runtime                      |
| 480_060_000 | Schemata.Flow.Bpmn                 | Flow.Bpmn                | Full BPMN 2.0.2 engine (multi-token, subprocesses, compensation, transactions) |
| 480_100_000 | Schemata.Flow.Http                 | Flow (`MapHttp`)     | HTTP resource bridge for process instances and transitions         |
| 480_200_000 | Schemata.Flow.Grpc                 | Flow (`MapGrpc`)     | gRPC resource bridge for process instances and transitions         |
| 480_300_000 | Schemata.Flow.Event                | Flow.Event               | Bridges BPMN message/signal catches to the event bus               |
| 480_400_000 | Schemata.Flow.Scheduling           | Flow.Scheduling          | Bridges BPMN timer catches to the scheduler                        |
| 490_000_000 | Schemata.Resource.Foundation       | Resource                 | Google AIP-compliant resource service                              |
| 490_100_000 | Schemata.Resource.Http             | Resource (`MapHttp`)     | HTTP/REST endpoint                                                 |
| 490_200_000 | Schemata.Resource.Grpc             | Resource (`MapGrpc`)     | gRPC endpoint                                                      |
| 500_000_000 | Schemata.Report.Foundation         | Report                   | Report definitions, snapshots, and generation                     |
| 500_100_000 | Schemata.Report.Http               | Report (`MapHttp`)       | HTTP resource bridge for reports and snapshots                    |
| 500_200_000 | Schemata.Report.Grpc               | Report (`MapGrpc`)       | gRPC resource bridge for reports and snapshots                    |
| 500_400_000 | Schemata.Report.Scheduling         | Report.Scheduling        | Periodic report generation bridge                                  |
| 520_000_000 | Schemata.Modular                   | Modular                  | Module discovery and loading                                       |

## Compliance

Schemata targets the latest .NET Long-Term Support (LTS) version and the most recent .NET release. All runtime packages target `net8.0;net10.0`. Source generators target `netstandard2.0` so Roslyn can load them.

| Package                     | Compliance                                                                                                                        |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| Schemata.Advice.Generator   | ![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)                                                   |
| Schemata.Modeling.Generator | ![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)                                                   |
| All other packages | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |

### Schemata.Authorization.Foundation

Schemata Authorization Foundation complies with the [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html) specification.

### Schemata.Identity.Foundation

Schemata Identity Foundation is compatible with ASP.NET Core Identity.

### Schemata.Mapping.Foundation

The Schemata Mapping Foundation is compatible with various mapping libraries, including [AutoMapper](https://www.nuget.org/packages/AutoMapper/) and [Mapster](https://www.nuget.org/packages/Mapster/), among others.

It provides a unified interface for these libraries, enabling developers to switch between them without modifying application code.

### Schemata.Resource.Foundation

The Schemata Resource Foundation complies with the [API Improvement Proposals - General AIPs](https://google.aip.dev/general) proposals. It builds on `Schemata.Mapping.Foundation` for object mapping and no longer requires `Schemata.Security.Foundation`.

### Schemata.Flow.Foundation

The default `StateMachineEngine` in `Schemata.Flow.StateMachine` runs a subset of [BPMN 2.0.2](https://www.omg.org/spec/BPMN/2.0.2/): one start event, at least one end event, plain activities (no `SubProcess` / `CallActivity` / loop characteristics), `ExclusiveGateway`, `EventBasedGateway` (exclusive mode only), interrupting boundary events, and intermediate catch events reachable from an `EventBasedGateway`. The full BPMN AST in `Schemata.Flow.Skeleton` covers more (parallel / inclusive / complex gateways, subprocesses, multi-instance loops) and is intended for alternate engines plugged in via a keyed `IFlowRuntime`.

Intermediate catch events bridge to runtime infrastructure: `Schemata.Flow.Event` correlates `Message` and `Signal` catches with the event bus, and `Schemata.Flow.Scheduling` fires `Timer` catches through the scheduler.

The process graph is built with a strongly-typed C# DSL in `Schemata.Flow.Skeleton.Builders` (`ProcessBuilder`, `ActivityBehavior`, `BoundaryCatch`, `EventBranch`, `FlowBranch`, `InclusiveBranch`, `InclusiveMerge`, `ParallelFork`, `ParallelJoin`, `StartFlow`).

### Schemata.Flow.Bpmn

`Schemata.Flow.Bpmn` provides the full BPMN 2.0.2 engine for multi-token execution, including parallel, inclusive, and complex gateways; embedded, event, and transaction subprocesses; `CallActivity`; standard loops; sequential and parallel multi-instance loops; interrupting and non-interrupting boundary events; escalation; and compensation. See [BPMN engine](docs/documents/flow/bpmn-engine.md) for the feature matrix and runtime semantics.

The BPMN MIWG conformance suite covers an executable subset of the spec. Collaboration, lane, and related multi-process interchange cases are structurally out of scope for this single-process engine.

`Schemata.Flow.Bpmn` is intentionally not bundled into any meta target package. Consumers add it explicitly with `<PackageReference Include="Schemata.Flow.Bpmn" />`.
