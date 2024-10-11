# Schemata Module

Creating Schemata Module for Domain Driven & Modular Design.

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cyprincess/Schemata/build.yml)](https://github.com/Cyprincess/Schemata/actions/workflows/build.yml)
[![Codecov](https://img.shields.io/codecov/c/github/Cyprincess/Schemata.svg)](https://codecov.io/gh/Cyprincess/Schemata)
[![license](https://img.shields.io/github/license/Cyprincess/Schemata.svg)](https://github.com/Cyprincess/Schemata/blob/master/LICENSE)

## Quick Start

```shell
dotnet new classlib
dotnet add package --prerelease Schemata.Module.Complex.Targets
```

```csharp
public class Module : ModuleBase
{
    public override int Order => 0;

    public override int Priority => 1;

    public void ConfigureServices(IServiceCollection services) {
        //
    }

    public void Configure(IApplicationBuilder app, IServiceProvider sp) {
        //
    }
}
```
