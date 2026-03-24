# Schemata

A .NET application framework for building modular, extensible business applications.

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cyprincess/Schemata/build.yml)](https://github.com/Cyprincess/Schemata/actions/workflows/build.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=Schemata&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Schemata)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=Schemata&metric=coverage)](https://sonarcloud.io/summary/new_code?id=Schemata)
[![license](https://img.shields.io/github/license/Cyprincess/Schemata.svg)](https://github.com/Cyprincess/Schemata/blob/master/LICENSE)
![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)
![netstandard2.1](https://img.shields.io/badge/netstandard-2.1-brightgreen.svg)
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

- [DSL](https://nuget.org/packages/Schemata.Modeling.Generator)
- [Modular](https://nuget.org/packages/Schemata.Module.Complex.Targets)
- Audit
- [Authorization](https://nuget.org/packages/Schemata.Authorization.Foundation)
- Datasource
- Event
- [Identity](https://nuget.org/packages/Schemata.Identity.Foundation)
- [Mapping](https://nuget.org/packages/Schemata.Mapping.Foundation)
- [Repository](https://nuget.org/packages/Schemata.Entity.Repository)
- Task
- [Tenant](https://nuget.org/packages/Schemata.Tenancy.Foundation)
- [Validation](https://nuget.org/packages/Schemata.Validation.FluentValidation)
- [Workflow](https://nuget.org/packages/Schemata.Workflow.Foundation)

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
| 120_000_000 | ExceptionHandler       | ASP.NET Exception Handler Middleware                                                 |
| 130_000_000 | Logging                | ASP.NET Logging Middleware                                                           |
| 140_000_000 | HttpLogging            | ASP.NET HTTP Logging Middleware                                                      |
| 150_000_000 | W3CLogging             | ASP.NET W3C Logging Middleware                                                       |
| 160_000_000 | Https                  | ASP.NET HTTPS & HTTPS Redirection Middlewares                                        |
| 170_000_000 | Tenancy                | Multi-tenant isolation middleware (Order: 900_000_000)                               |
| 180_000_000 | CookiePolicy           | ASP.NET Cookie Policy Middleware                                                     |
| 190_000_000 | Routing                | ASP.NET Routing Middleware                                                           |
| 200_000_000 | Quota                  | ASP.NET Rate Limiter Middleware                                                      |
| 210_000_000 | Cors                   | ASP.NET CORS Middleware                                                              |
| 220_000_000 | Authentication         | ASP.NET Authentication & Authorization Middlewares                                   |
| 230_000_000 | Session                | ASP.NET Session Middleware                                                           |
| 240_000_000 | Controllers            | ASP.NET MVC Middlewares, without Views                                               |
| 250_000_000 | JsonSerializer         | Configure System.Text.Json to use snake_case and handle JavaScript's 53-bit integers |

### Extension Features

An extension feature can be activated in the same way as a built-in feature.

| Priority    | Package                           | Feature              | Description                           |
| ----------- | --------------------------------- | -------------------- | ------------------------------------- |
| 400_000_000 | Schemata.Security.Foundation      | Security             | RBAC/ABAC security policies           |
| 410_000_000 | Schemata.Identity.Foundation      | Identity             | ASP.NET Core Identity integration     |
| 420_000_000 | Schemata.Authorization.Foundation | Authorization        | OAuth 2.0 / OpenID Connect server     |
| 430_000_000 | Schemata.Mapping.Foundation       | Mapping              | Unified object mapper abstraction     |
| 440_000_000 | Schemata.Workflow.Foundation      | Workflow             | Stateful workflow / state machine     |
| 450_000_000 | Schemata.Resource.Foundation      | Resource             | Google AIP-compliant resource service |
| 460_000_000 | Schemata.Resource.Http            | Resource (`MapHttp`) | HTTP/REST endpoint                    |
| 470_000_000 | Schemata.Resource.Grpc            | Resource (`MapGrpc`) | gRPC endpoint                         |
| 480_000_000 | Schemata.Modular                  | Modular              | Module discovery and loading          |

## Compliance

Schemata is designed to be compatible with .NET Standard 2.0, .NET Standard 2.1, the latest .NET Long-Term Support (LTS) version, and the most recent .NET release.

Some packages may have additional compliance requirements, which are documented below.

| Package                           | Compliance                                                                                                                        |
| --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| Schemata.Modeling.Generator       | ![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)                                                   |
| Schemata.Core                     | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |
| Schemata.Modular                  | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |
| Schemata.Authorization.Foundation | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |
| Schemata.Identity.Foundation      | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |
| Schemata.Mapping.Foundation       | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |
| Schemata.Resource.Foundation      | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |
| Schemata.Security.Foundation      | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |
| Schemata.Tenancy.Foundation       | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |
| Schemata.Workflow.Foundation      | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net10.0](https://img.shields.io/badge/Net-10.0-brightgreen.svg) |

### Schemata.Authorization.Foundation

Schemata Authorization Foundation complies with the [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html) specification.

### Schemata.Identity.Foundation

Schemata Identity Foundation is compatible with ASP.NET Core Identity.

### Schemata.Mapping.Foundation

The Schemata Mapping Foundation is compatible with various mapping libraries, including [AutoMapper](https://www.nuget.org/packages/AutoMapper/) and [Mapster](https://www.nuget.org/packages/Mapster/), among others.

It provides a unified interface for these libraries, enabling developers to switch between them without modifying application code.

### Schemata.Resource.Foundation

The Schemata Resource Foundation complies with the [API Improvement Proposals - General AIPs](https://google.aip.dev/general) proposals.

### Schemata.Workflow.Foundation

Unfortunately, the Schemata Workflow Foundation is not yet compliant with enterprise standards such as [BPMN 2.0](https://www.omg.org/spec/BPMN/2.0.2/).
