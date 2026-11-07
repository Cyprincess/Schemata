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
- [Caching](https://nuget.org/packages/Schemata.Caching.Skeleton) — scalar, atomic and collection cache operations through Memory or Redis providers
- [DSL](https://nuget.org/packages/Schemata.Modeling.Generator) — `.skm` source generator
- [Event](https://nuget.org/packages/Schemata.Event.Foundation) — in-process / RabbitMQ event bus
- [Flow](https://nuget.org/packages/Schemata.Flow.Foundation) — BPMN process engine, HTTP/gRPC transports, event/scheduling bridges
- [Identity](https://nuget.org/packages/Schemata.Identity.Foundation) — ASP.NET Core Identity integration
- [Mapping](https://nuget.org/packages/Schemata.Mapping.Foundation) — unified object-mapper abstraction (AutoMapper / Mapster)
- [Modular](https://nuget.org/packages/Schemata.Module.Complex.Targets) — module discovery and loading
- [Repository](https://nuget.org/packages/Schemata.Entity.Repository): EF Core / LinqToDB providers, advisor pipelines, units of work, and query caching.
- [Resource](https://nuget.org/packages/Schemata.Resource.Foundation) — Google AIP-compliant CRUD service over HTTP and gRPC
- [Scheduling](https://nuget.org/packages/Schemata.Scheduling.Foundation) — persistent cron / periodic / one-time job scheduler
- [Tenancy](https://nuget.org/packages/Schemata.Tenancy.Foundation) — multi-tenant resolution and per-tenant DI
- [Validation](https://nuget.org/packages/Schemata.Validation.FluentValidation) — FluentValidation integration

Scheduling packages are intentionally not bundled into any meta target package, like `Schemata.Flow.Bpmn`. Consumers add the required scheduling package explicitly with `<PackageReference />`.

The [package inventory](docs/documents/packages.md) records 90 source package projects, 10 target
projects, two generators, and 68 test projects, with current layer and optional-adapter lists.

## Features

Features are modular components that can be integrated at startup.

Features are characterized by `Order` and `Priority`, both of which are `Int32` values. `Order` controls the sequence of `ConfigureServices` calls; `Priority` controls the sequence of `ConfigureApplication` and `ConfigureEndpoints` calls.

The range `[100_000_000, 900_000_000]` for `Order` and `Priority` is reserved for built-in features and Schemata extensions.

### Feature priorities

The [built-in and extension feature reference](docs/documents/core/built-in-features.md)
contains the authoritative priority table and activation details.

## Compliance

Schemata targets the latest .NET Long-Term Support (LTS) version and the most recent .NET release. All runtime packages target `net8.0;net10.0`. Source generators target `netstandard2.0` so Roslyn can load them.

| Package                     | Compliance                                                                                                                        |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| Schemata.Advice.Generator   | ![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)                                                   |
| Schemata.Modeling.Generator | ![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)                                                   |
| All other packages | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |

### Schemata.Authorization.Foundation

Schemata Authorization Foundation provides opt-in OAuth 2.0 and OpenID Connect capabilities with partial, configuration-dependent standards coverage. See the [requirement-level protocol matrix](docs/documents/authorization.md#protocol-requirement-matrix) and [browser evidence matrix](docs/documents/authorization.md#browser-and-external-rp-evidence-matrix) for supported behavior, application obligations, and historical downgrades. These describe bounded evidence rather than OpenID certification.

### Schemata.Identity.Foundation

Schemata Identity Foundation is compatible with ASP.NET Core Identity.

### Schemata.Mapping.Foundation

The Schemata Mapping Foundation is compatible with various mapping libraries, including [AutoMapper](https://www.nuget.org/packages/AutoMapper/) and [Mapster](https://www.nuget.org/packages/Mapster/), among others.

It provides a unified interface for these libraries, enabling developers to switch between them without modifying application code.

### Schemata.Resource.Foundation

Schemata Resource Foundation provides code-first resource operations with partial Google AIP coverage. The requirement matrices for [modeling](docs/documents/resource/aip-modeling.md#requirement-matrix), [interactions](docs/documents/resource/aip-interactions.md), and [business logic](docs/documents/resource/aip-business-logic.md) distinguish enforced behavior, application responsibilities, and unimplemented contracts on HTTP and gRPC. It uses `Schemata.Mapping.Foundation` for object mapping; security is activated separately.

### Schemata.Flow.Foundation

The default `StateMachineEngine` in `Schemata.Flow.StateMachine` runs a subset of [BPMN 2.0.2](https://www.omg.org/spec/BPMN/2.0.2/): one start event, at least one end event, plain activities (no `SubProcess` / `CallActivity` / loop characteristics), `ExclusiveGateway`, `EventBasedGateway` (exclusive mode only), interrupting boundary events, and intermediate catch events reachable from an `EventBasedGateway`. The full BPMN AST in `Schemata.Flow.Skeleton` covers more (parallel / inclusive / complex gateways, subprocesses, multi-instance loops) and is intended for alternate engines plugged in via a keyed `IFlowRuntime`.

Intermediate catch events bridge to runtime infrastructure: `Schemata.Flow.Event` correlates `Message` and `Signal` catches with the event bus, and `Schemata.Flow.Scheduling` fires `Timer` catches through the scheduler.

The process graph is built with a strongly-typed C# DSL in `Schemata.Flow.Skeleton.Builders` (`ProcessBuilder`, `ActivityBehavior`, `BoundaryCatch`, `EventBranch`, `FlowBranch`, `InclusiveBranch`, `InclusiveMerge`, `ParallelFork`, `ParallelJoin`, `StartFlow`).

### Schemata.Flow.Bpmn

`Schemata.Flow.Bpmn` provides a multi-token engine for an executable BPMN subset, including gateways, subprocesses, loops, boundary paths, escalation, and compensation within the limits in the [BPMN engine feature matrix](docs/documents/flow/bpmn-engine.md#feature-matrix). The matrix describes runtime paths and exclusions rather than full BPMN 2.0.2 conformance.

The BPMN MIWG conformance suite covers an executable subset of the spec. Collaboration, lane, and related multi-process interchange cases are structurally out of scope for this single-process engine.

`Schemata.Flow.Bpmn` is intentionally not bundled into any meta target package. Consumers add it explicitly with `<PackageReference Include="Schemata.Flow.Bpmn" />`.
