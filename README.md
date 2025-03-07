# Schemata

Application Framework aims on modular business applications.

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cyprincess/Schemata/build.yml)](https://github.com/Cyprincess/Schemata/actions/workflows/build.yml)
[![Codecov](https://img.shields.io/codecov/c/github/Cyprincess/Schemata.svg)](https://codecov.io/gh/Cyprincess/Schemata)
[![license](https://img.shields.io/github/license/Cyprincess/Schemata.svg)](https://github.com/Cyprincess/Schemata/blob/master/LICENSE)

![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)
![netstandard2.1](https://img.shields.io/badge/netstandard-2.1-brightgreen.svg)
![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg)
![net9.0](https://img.shields.io/badge/Net-9.0-brightgreen.svg)

## Quick Start

```shell
dotnet new web
dotnet add package --prerelease Schemata.Application.Complex.Targets
```

```csharp
var builder = WebApplication.CreateBuilder(args)
                            .UseSchemata(schema => {
                                 schema.UseLogging();
                                 schema.UseDeveloperExceptionPage();

                                 schema.ConfigureServices(services => {
                                     services.AddTransient(typeof(IRepositoryAddAsyncAdvice<>), typeof(MyAdviceAddAsync<>));

                                     services.AddRepository(typeof(MyRepository<>))
                                             .UseEntityFrameworkCore<MyDbContext>((sp, options) => options.UseSqlServer(schema.Configuration.GetConnectionString("Default")));

                                     services.AddDistributedMemoryCache();
                                 });

                                 schema.UseForwardedHeaders();
                                 schema.UseHttps();
                                 schema.UseCookiePolicy();

                                 schema.UseSession();

                                 schema.UseCors();
                                 schema.UseRouting();
                                 schema.UseControllers();
                                 schema.UseJsonSerializer();

                                 schema.UseTenancy()
                                       .UseHostResolver();
                                 schema.UseModular();

                                 schema.UseSecurity();
                                 schema.UseIdentity();
                                 schema.UseAuthorization(options => {
                                            options.AddEphemeralEncryptionKey()
                                                   .AddEphemeralSigningKey();
                                        })
                                       .UseCodeFlow()
                                       .UseRefreshTokenFlow()
                                       .UseDeviceFlow()
                                       .UseIntrospection()
                                       .UseCaching();
                                 schema.UseWorkflow();

                                 // You can also utilize UseAutoMapper() once you've incorporated the Schemata.Mapping.AutoMapper package into your project.
                                 schema.UseMapster()
                                       .Map<Source, Destination>(map => {
                                            map.For(d => d.DisplayName).From(s => s.Name);
                                            map.For(d => d.Age).From(s => s.Age).Ignore((s, d) => s.Age < 18);
                                            map.For(d => d.Grade).Ignore()
                                               .For(d => d.Sex).From(s => s.Sex.ToString());
                                        });

                                 schema.UseResource()
                                       .MapHttp()
                             });

var app = builder.Build();

app.Run();
```

## Fields

- [DSL](https://nuget.org/packages/Schemata.DSL)
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
- [Validation](https://nuget.org/packages/Schemata.Validation)
- [Workflow](https://nuget.org/packages/Schemata.Workflow.Foundation)

## Features

Features are modular components that can be integrated during the application startup process.

Each feature must implement the `ISimpleFeature` interface.

Features are characterized by `Order` and `Priority`, both of which are `Int32` values. The `Order` determines the
sequence in which the `ConfigureServices` methods are invoked. The `Priority` establishes the sequence for invoking
the `Configure<Application|Endpoints>` methods.

The range `[100_000_000, 1_000_000_000)` and `(2_147_000_000, 2_147_400_000]` for `Order` and `Priority` is reserved for
built-in features and Schemata extensions.

### Built-in Features

A built-in feature can be activated by calling the `UseXXX` method on the `SchemataBuilder` instance. These features may
also have additional configuration methods.

| Priority    | Feature                | Description                                                                           |
|-------------|------------------------|---------------------------------------------------------------------------------------|
| 100_010_000 | ExceptionHandler       | Asp.Net Exception Handler Middleware                                                  |
| 100_110_000 | Logging                | Asp.Net Logging Middleware                                                            |
| 100_120_000 | HttpLogging            | Asp.Net HTTP Logging Middleware                                                       |
| 100_130_000 | W3CLogging             | Asp.Net W3C Logging Middleware                                                        |
| 110_000_000 | DeveloperExceptionPage | Asp.Net Developer Exception Page Middleware                                           |
| 111_000_000 | ForwardedHeaders       | Asp.Net Forwarded Headers Middleware                                                  |
| 120_000_000 | Https                  | Asp.Net HTTPS & HTTPS Redirection Middlewares                                         |
| 130_000_000 | CookiePolicy           | Asp.Net Cookie Policy Middleware                                                      |
| 140_000_000 | Routing                | Asp.Net Routing Middleware                                                            |
| 141_100_000 | Quota                  | Asp.Net Rate Limiter Middleware                                                       |
| 150_000_000 | Cors                   | Asp.Net CORS Middleware                                                               |
| 160_000_000 | Authentication         | Asp.Net Authentication & Authorization Middlewares                                    |
| 170_000_000 | Session                | Asp.Net Session Middleware                                                            |
| 210_000_000 | Controllers            | Asp.Net MVC Middlewares, without Views                                                |
| 210_100_000 | JsonSerializer         | Configure System.Text.Json to use snake_case and handle JavaScript's 53-bits integers |

### Extension Features

An extension feature can be activated in the same way as a built-in feature.

| Priority      | Package                           | Feature              | Description                        |
|---------------|-----------------------------------|----------------------|------------------------------------|
| 300_100_000   | Schemata.Security.Foundation      | Security             | Schemata Security Foundation       |
| 310_000_000   | Schemata.Identity.Foundation      | Identity             | Schemata Identity Foundation       |
| 320_000_000   | Schemata.Authorization.Foundation | Authorization        | Schemata Authorization Foundation  |
| 340_000_000   | Schemata.Mapping.Foundation       | Mapping              | Schemata Mapper Foundation         |
| 350_000_000   | Schemata.Workflow.Foundation      | Workflow             | Schemata Workflow Foundation       |
| 360_000_000   | Schemata.Resource.Foundation      | Resource             | Schemata Resource Service          |
| 360_100_000   | Schemata.Resource.Http            | Resource (`MapHttp`) | Schemata Resource Service for HTTP |
| 2_147_100_000 | Schemata.Tenancy.Foundation       | Tenancy              | Schemata Tenancy Foundation        |
| 2_147_200_000 | Schemata.Modular                  | Modular              | Modularization                     |

## Compliance

Schemata is designed to be compatible with .NET Standard 2.0, .NET Standard 2.1, the latest .NET Long-Term Support (LTS)
version, and the most recent .NET version.

Some packages may have additional compliance requirements, which are documented below.

| Package                           | Compliance                                                                                                                      |
|-----------------------------------|---------------------------------------------------------------------------------------------------------------------------------|
| Schemata.DSL                      | ![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)                                                 |
| Schemata.Core                     | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net9.0](https://img.shields.io/badge/Net-9.0-brightgreen.svg) |
| Schemata.Modular                  | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net9.0](https://img.shields.io/badge/Net-9.0-brightgreen.svg) |
| Schemata.Authorization.Foundation | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net9.0](https://img.shields.io/badge/Net-9.0-brightgreen.svg) |
| Schemata.Identity.Foundation      | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net9.0](https://img.shields.io/badge/Net-9.0-brightgreen.svg) |
| Schemata.Mapping.Foundation       | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net9.0](https://img.shields.io/badge/Net-9.0-brightgreen.svg) |
| Schemata.Resource.Foundation      | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net9.0](https://img.shields.io/badge/Net-9.0-brightgreen.svg) |
| Schemata.Security.Foundation      | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net9.0](https://img.shields.io/badge/Net-9.0-brightgreen.svg) |
| Schemata.Tenancy.Foundation       | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net9.0](https://img.shields.io/badge/Net-9.0-brightgreen.svg) |
| Schemata.Workflow.Foundation      | ![net8.0](https://img.shields.io/badge/Net-8.0-brightgreen.svg) ![net9.0](https://img.shields.io/badge/Net-9.0-brightgreen.svg) |

### Schemata.Authorization.Foundation

Schemata Authorization Foundation is designed to comply with
the [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html) specification.

### Schemata.Identity.Foundation

Schemata Identity Foundation is designed to comply with Asp.Net Core Identity.

Additionally, we bring
the [Bearer Token Authentication Scheme](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.bearertokenextensions.addbearertoken?view=aspnetcore-8.0)
and [Core Identity API](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-8.0)
to platforms that do not support it.

### Schemata.Mapping.Foundation

The Schemata Mapping Foundation is designed to be compatible with various mapping libraries,
including [AutoMapper](https://www.nuget.org/packages/AutoMapper/)
and [Mapster](https://www.nuget.org/packages/Mapster/), among others.

It provides a unified interface for these libraries, enabling developers to switch between them without modifying their
codes.

### Schemata.Resource.Foundation

The Schemata Resource Foundation is designed to comply with
the [API Improvement Proposals - General AIPs](https://google.aip.dev/general) proposals.

### Schemata.Workflow.Foundation

Unfortunately, the Schemata Workflow Foundation is not yet compliant with enterprise standards such
as [BPMN 2.0](https://www.omg.org/spec/BPMN/2.0.2/).
