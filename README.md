# Schemata

Application Framework aims on modular business applications.

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cyprincess/Schemata/build.yml)](https://github.com/Cyprincess/Schemata/actions/workflows/build.yml)
[![Codecov](https://img.shields.io/codecov/c/github/Cyprincess/Schemata.svg)](https://codecov.io/gh/Cyprincess/Schemata)
[![license](https://img.shields.io/github/license/Cyprincess/Schemata.svg)](https://github.com/Cyprincess/Schemata/blob/master/LICENSE)

## Quick Start

```shell
dotnet new web
dotnet add package --prerelease Schemata.Application.Complex.Targets
```

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args)
                            .UseSchemata(schema => {
                                 schema.UseLogging();

                                 schema.UseDeveloperExceptionPage();
                                 schema.UseHttps();
                                 schema.UseCookiePolicy();
                                 schema.UseRouting();
                                 schema.UseCors();
                                 schema.UseAuthentication(authenticate => {
                                     authenticate.AddCookie();
                                 });

                                 schema.ConfigureServices(services => {
                                     services.AddDistributedMemoryCache();
                                 });
                                 schema.UseSession();

                                 schema.UseControllers();

                                 schema.UseModular();
                             });

var app = builder.Build();

app.Run();
```

## Fields

- [DSL](https://nuget.org/packages/Schemata.DSL)
- [Modular](https://nuget.org/packages/Schemata.Module.Complex.Targets)
- Audit
- Datasource
- Event
- Task
- Tenant
- Validation
- Workflow

## Features

Features are components that can be added to the application startup.

All features must implement the `ISimpleFeature` interface.

Features have `Order` and `Priority`, which are `Int32` values. The `Order` is used to determine the order
to `ConfigureServices` methods are called. The `Priority` is used to determine the order
to `Configure<Application|Endpoints>` methods are called.

`Order` and `Priority` between `[0, 1_000_000_000)` are reserved for built-in and Schemata extensions.

### Built-in Features

A built-in feature can be activated by calling the `Use` method on the `SchemataBuilder` instance, they may have
additional configuration methods.

| Priority    | Feature                | Description                                        |
|-------------|------------------------|----------------------------------------------------|
| 100_110_000 | Logging                | Asp.Net Logging Middleware                         |
| 100_120_000 | HttpLogging            | Asp.Net HTTP Logging Middleware                    |
| 100_130_000 | W3CLogging             | Asp.Net W3C Logging Middleware                     |
| 110_000_000 | DeveloperExceptionPage | Asp.Net Developer Exception Page Middleware        |
| 120_000_000 | Https                  | Asp.Net HTTPS & HTTPS Redirection Middlewares      |
| 130_000_000 | CookiePolicy           | Asp.Net Cookie Policy Middleware                   |
| 140_000_000 | Routing                | Asp.Net Routing Middleware                         |
| 141_100_000 | Quota                  | Asp.Net Rate Limiter Middleware                    |
| 150_000_000 | Cors                   | Asp.Net CORS Middleware                            |
| 160_000_000 | Authentication         | Asp.Net Authentication & Authorization Middlewares |
| 170_000_000 | Session                | Asp.Net Session Middleware                         |
| 210_000_000 | Controllers            | Asp.Net MVC Middlewares, without Views             |
