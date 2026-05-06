# Schemata Business

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Cyprincess/Schemata/build.yml)](https://github.com/Cyprincess/Schemata/actions/workflows/build.yml)
[![Codecov](https://img.shields.io/codecov/c/github/Cyprincess/Schemata.svg)](https://codecov.io/gh/Cyprincess/Schemata)
[![license](https://img.shields.io/github/license/Cyprincess/Schemata.svg)](https://github.com/Cyprincess/Schemata/blob/master/LICENSE)

![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-brightgreen.svg)
![netstandard2.1](https://img.shields.io/badge/netstandard-2.1-brightgreen.svg)

A Schemata Business project is a class library that defines domain models, repository contracts, and business logic for a single domain area. It has no dependency on ASP.NET Core — only on the framework's portable abstractions — so it can be shared across application projects and modules.

## Package Variants

Pick the variant that matches the capabilities you need:

| Package                                | What it includes                                                            |
| -------------------------------------- | --------------------------------------------------------------------------- |
| `Schemata.Business.Targets`            | Base: Abstractions                                                          |
| `Schemata.Business.Persisting.Targets` | Base + Repository pattern                                                   |
| `Schemata.Business.Complex.Targets`    | Persisting + DSL + Mapping + Authorization + Identity + Security + Flow + Workflow |

## Quick Start

```shell
dotnet new classlib

# Base — abstractions only
dotnet add package --prerelease Schemata.Business.Targets

# With data persistence
dotnet add package --prerelease Schemata.Business.Persisting.Targets

# Full suite (DSL, mapping, auth, workflow)
dotnet add package --prerelease Schemata.Business.Complex.Targets
```

## See Also

- [Schemata.Application.Complex.Targets](https://nuget.org/packages/Schemata.Application.Complex.Targets) — host application targets
- [Schemata.Module.Complex.Targets](https://nuget.org/packages/Schemata.Module.Complex.Targets) — module project targets
- [Schemata.Modeling.Generator](https://nuget.org/packages/Schemata.Modeling.Generator) — SKM schema DSL (included in Complex)
