# Schemata Module

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cyprincess/Schemata/build.yml)](https://github.com/Cyprincess/Schemata/actions/workflows/build.yml)
[![Codecov](https://img.shields.io/codecov/c/github/Cyprincess/Schemata.svg)](https://codecov.io/gh/Cyprincess/Schemata)
[![license](https://img.shields.io/github/license/Cyprincess/Schemata.svg)](https://github.com/Cyprincess/Schemata/blob/master/LICENSE)

A Schemata Module is a self-contained plugin that integrates into a host application via `Schemata.Modular`. Each module exposes two startup hooks — `ConfigureServices` for DI registration and `Configure` for pipeline setup — and executes in `Order`/`Priority` sequence alongside the host's own features.

## Package Variants

Pick the variant that matches the capabilities you need:

| Package                              | What it includes                                                                         |
| ------------------------------------ | ---------------------------------------------------------------------------------------- |
| `Schemata.Module.Targets`            | Base: Abstractions                                                                       |
| `Schemata.Module.Persisting.Targets` | Base + Repository pattern                                                                |
| `Schemata.Module.Complex.Targets`    | Persisting + DSL + Mapping + Authorization + Identity + Security + Validation + Flow + Workflow |

## Quick Start

```shell
dotnet new classlib
dotnet add package --prerelease Schemata.Module.Complex.Targets
```

```csharp
public sealed class MyModule : ModuleBase
{
    public override int Order => 0;

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // Register services for this module
    }

    public void ConfigureApplication(IApplicationBuilder app)
    {
        // Configure the request pipeline
    }
}
```

The host application picks up modules automatically when `UseModular()` is called on the `SchemataBuilder`.

## See Also

- [Schemata.Modular](https://nuget.org/packages/Schemata.Modular) — the runtime that loads and invokes modules
- [Schemata.Business.Complex.Targets](https://nuget.org/packages/Schemata.Business.Complex.Targets) — business library targets
- [Schemata.Application.Complex.Targets](https://nuget.org/packages/Schemata.Application.Complex.Targets) — host application targets
- [Schemata.Modeling.Generator](https://nuget.org/packages/Schemata.Modeling.Generator) — SKM schema DSL (included in Complex)
