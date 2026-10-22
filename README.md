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
- [Repository](https://nuget.org/packages/Schemata.Entity.Repository) — EF Core / LinqToDB providers with advisor pipeline, unit of work, ownership, query caching
- [Resource](https://nuget.org/packages/Schemata.Resource.Foundation) — AIP-aligned CRUD service over HTTP and gRPC (requirement-level status in [AIP Modeling](docs/documents/resource/aip-modeling.md#requirement-matrix) and [AIP Interactions](docs/documents/resource/aip-interactions.md))
- [Scheduling](https://nuget.org/packages/Schemata.Scheduling.Foundation) — persistent cron / periodic / one-time job scheduler
- [Tenancy](https://nuget.org/packages/Schemata.Tenancy.Foundation) — multi-tenant resolution and per-tenant DI
- [Validation](https://nuget.org/packages/Schemata.Validation.FluentValidation) — FluentValidation integration

Scheduling packages are intentionally not bundled into any meta target package, like `Schemata.Flow.Bpmn`. Consumers add the required scheduling package explicitly with `<PackageReference />`.

## Features

Features are modular components that can be integrated at startup.

Features are characterized by `Order` and `Priority`, both of which are `Int32` values. `Order` controls the sequence of `ConfigureServices` calls; `Priority` controls the sequence of `ConfigureApplication` and `ConfigureEndpoints` calls.

The range `[100_000_000, 900_000_000]` for `Order` and `Priority` is reserved for built-in features and Schemata extensions.

### Feature priority

Each feature declares its `Priority` in code. The single canonical table — every built-in and
extension feature class, its `Priority`, its `Use*()` activation, and what it registers — lives in
[Built-in Features](docs/documents/core/built-in-features.md). It is generated from the HEAD feature
constants and is the only table to update when a priority changes; this README and the documentation
home link to it instead of duplicating it.

## Compliance

Schemata targets the latest .NET Long-Term Support (LTS) version and the most recent .NET release. All runtime packages target `net8.0;net10.0`. Source generators target `netstandard2.0` so Roslyn can load them.

| Package                     | Compliance                                                                                                                        |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| Schemata.Advice.Generator   | ![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)                                                   |
| Schemata.Modeling.Generator | ![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)                                                   |
| All other packages | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |

### Schemata.Authorization.Foundation

Schemata Authorization Foundation implements the OAuth 2.0 / OpenID Connect endpoints and rules
graded requirement by requirement in
[Authorization § Standards compliance](docs/documents/authorization.md#standards-compliance). That
matrix records each row as Enforced, Partial, or Application responsibility — the project does not
claim whole-specification conformance — and discloses the opt-in implicit/hybrid/none degradation
flows against RFC 9700 and RFC 10017.

### Schemata.Identity.Foundation

Schemata Identity Foundation is compatible with ASP.NET Core Identity.

### Schemata.Mapping.Foundation

The Schemata Mapping Foundation is compatible with various mapping libraries, including [AutoMapper](https://www.nuget.org/packages/AutoMapper/) and [Mapster](https://www.nuget.org/packages/Mapster/), among others.

It provides a unified interface for these libraries, enabling developers to switch between them without modifying application code.

### Schemata.Resource.Foundation

The Schemata Resource Foundation implements AIP-aligned resource primitives. Each AIP is graded
requirement by requirement in [AIP Modeling](docs/documents/resource/aip-modeling.md#requirement-matrix),
[AIP Interactions](docs/documents/resource/aip-interactions.md), and
[AIP Business Logic](docs/documents/resource/aip-business-logic.md); the matrices state the Partial,
Not implemented, and application-responsibility rows rather than claiming blanket conformance. The
package maps through `Schemata.Mapping.Skeleton` (implemented by the `Schemata.Mapping.*` adapters)
and integrates the shared security seams in `Schemata.Security.Skeleton` / `Schemata.Security.Foundation`;
resource authorization remains an explicit `UseSecurity()` + `WithAuthorization()` activation.

### Schemata.Flow.Foundation

The default `StateMachineEngine` in `Schemata.Flow.StateMachine` runs a subset of [BPMN 2.0.2](https://www.omg.org/spec/BPMN/2.0.2/): one start event, at least one end event, plain activities (no `SubProcess` / `CallActivity` / loop characteristics), `ExclusiveGateway`, `EventBasedGateway` (exclusive mode only), interrupting boundary events, and intermediate catch events reachable from an `EventBasedGateway`. The full BPMN AST in `Schemata.Flow.Skeleton` covers more (parallel / inclusive / complex gateways, subprocesses, multi-instance loops) and is intended for alternate engines plugged in via a keyed `IFlowRuntime`.

Intermediate catch events bridge to runtime infrastructure: `Schemata.Flow.Event` correlates `Message` and `Signal` catches with the event bus, and `Schemata.Flow.Scheduling` fires `Timer` catches through the scheduler.

The process graph is built with a strongly-typed C# DSL in `Schemata.Flow.Skeleton.Builders` (`ProcessBuilder`, `ActivityBehavior`, `BoundaryCatch`, `EventBranch`, `FlowBranch`, `InclusiveBranch`, `InclusiveMerge`, `ParallelFork`, `ParallelJoin`, `StartFlow`).

### Schemata.Flow.Bpmn

`Schemata.Flow.Bpmn` is the multi-token BPMN 2.0.2 engine: it executes parallel, inclusive, and complex gateways; embedded, event, and transaction subprocesses; `CallActivity`; standard loops; sequential and parallel multi-instance loops; interrupting and non-interrupting boundary events; escalation; and compensation. The [BPMN feature matrix and limits](docs/documents/flow/bpmn-engine.md#feature-matrix) record every construct and the executable-subset boundaries — collaboration, pools, lanes, link events, data modelling, and ad-hoc subprocesses are explicitly out of scope, so this is a defined BPMN 2.0.2 implementation, not a claim over the whole specification.

The BPMN MIWG conformance suite covers an executable subset of the spec. Collaboration, lane, and related multi-process interchange cases are structurally out of scope for this single-process engine.

`Schemata.Flow.Bpmn` is intentionally not bundled into any meta target package. Consumers add it explicitly with `<PackageReference Include="Schemata.Flow.Bpmn" />`.
