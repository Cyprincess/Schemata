# Schemata

Application Framework aims on modular business applications.

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cypriness/Schemata/build.yml)](https://github.com/Cypriness/Schemata/actions/workflows/build.yml)
[![Codecov](https://img.shields.io/codecov/c/github/Cypriness/Schemata.svg)](https://codecov.io/gh/Cypriness/Schemata)
[![license](https://img.shields.io/github/license/Cypriness/Schemata.svg)](https://github.com/Cypriness/Schemata/blob/master/LICENSE)

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

                                 schema.UsePage();
                                 schema.UseController();

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
