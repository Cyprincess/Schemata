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
                                 schema.UseHttpLogging();
                                 schema.UseW3CLogging();

                                 schema.UseDeveloperExceptionPage();
                                 schema.UseHttps();
                                 schema.UseStaticFiles();
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

## Features

- [DSL](https://nuget.org/packages/Schemata.DSL)
- [Modular](https://nuget.org/packages/Schemata.Module.Complex.Targets)
- Audit
- Datasource
- Event
- Task
- Tenant
- Validation
- Workflow
