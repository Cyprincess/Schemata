# Packages

Schemata ships three families of meta-package targets: Application, Business, and Module. Each family has base, Persisting, and Complex variants; Application also has a Modular variant. Each target `.csproj` sets MSBuild property flags, and the family `Directory.Build.props` translates those flags into `ProjectReference` entries. Target packages multi-target `net8.0;net10.0`, ship build assets, and pack `Schemata.Advice.Generator` as an analyzer.

## Package layers

Runtime packages follow the Skeleton / Foundation / Provider convention:

- `*.Skeleton` packages contain contracts, base types, attributes, options, and persisted entities that other layers can reference without pulling in runtime registration.
- `*.Foundation` packages contain concrete logic, default services, builders, feature registration, and owned cross-package wiring.
- Named provider and bridge packages (`*.Redis`, `*.RabbitMq`, `*.Http`, `*.Grpc`, `*.Scheduling`, `*.Event`) contain adapters that connect a foundation to a specific transport, broker, storage backend, or neighboring runtime.

This split keeps optional runtimes direct-referenceable. A provider depends on the skeleton contract it adapts; a foundation depends on its own skeleton and owns the default feature. Bridge packages join two foundations through a sub-builder, as `Schemata.Push.Scheduling` does for scheduled sends and `Schemata.Flow.Scheduling` does for timer catches.

## Where the code lives

| Folder | Files |
| --- | --- |
| `targets/Schemata.Application.Targets/` | `Schemata.Application.Targets.csproj`, `Schemata.Application.Persisting.Targets.csproj`, `Schemata.Application.Modular.Targets.csproj`, `Schemata.Application.Complex.Targets.csproj`, `Directory.Build.props` |
| `targets/Schemata.Business.Targets/` | `Schemata.Business.Targets.csproj`, `Schemata.Business.Persisting.Targets.csproj`, `Schemata.Business.Complex.Targets.csproj`, `Directory.Build.props` |
| `targets/Schemata.Module.Targets/` | `Schemata.Module.Targets.csproj`, `Schemata.Module.Persisting.Targets.csproj`, `Schemata.Module.Complex.Targets.csproj`, `Directory.Build.props` |

## MSBuild flag reference

Each `.csproj` sets one or more of these flags. The `Directory.Build.props` in the same folder translates each flag into a project reference or packed analyzer:

| Flag | Package or asset added |
| --- | --- |
| `UseDSLTargets=true` | Packs `Schemata.Modeling.Generator.dll` and `Parlot.dll` as analyzers |
| `UseModularTargets=true` | `Schemata.Modular` and `Schemata.Application.Modular.Targets.targets` under `build/` (Application only) |
| `UseTenancy=true` | `Schemata.Tenancy.Foundation` (Application only) |
| `UseAuthorization=true` | `Schemata.Authorization.Foundation` (Application) or `Schemata.Authorization.Skeleton` (Business/Module) |
| `UseIdentity=true` | `Schemata.Identity.Foundation` (Application) or `Schemata.Identity.Skeleton` (Business/Module) |
| `UseMapster=true` | `Schemata.Mapping.Mapster` (Application only) |
| `UseMapping=true` | `Schemata.Mapping.Skeleton` (Business/Module) |
| `UseRepository=true` | `Schemata.Entity.Repository` |
| `UseResourceGrpc=true` | `Schemata.Resource.Foundation` and `Schemata.Resource.Grpc` (Application only) |
| `UseResourceHttp=true` | `Schemata.Resource.Foundation` and `Schemata.Resource.Http` (Application only) |
| `UseSecurity=true` | `Schemata.Security.Foundation` (Application) or `Schemata.Security.Skeleton` (Business/Module) |
| `UseValidation=true` | `Schemata.Validation.FluentValidation` (Application/Module) |

## Application targets

Application targets reference `Schemata.Core` and include `Microsoft.AspNetCore.App`. Use them for host projects that create the ASP.NET Core application.

### Schemata.Application.Targets

Base application target. MSBuild flags: none beyond defaults.

Effective references:

- `Schemata.Core`
- `Schemata.Advice.Generator` (analyzer)

### Schemata.Application.Persisting.Targets

Adds the repository pattern. MSBuild flags: `UseRepository=true`.

Adds over base:

- `Schemata.Entity.Repository`

### Schemata.Application.Modular.Targets

Adds host-side module stamping. MSBuild flags: `UseModularTargets=true`.

Adds over base:

- `Schemata.Modular`

Also packs `Schemata.Application.Modular.Targets.targets` into the package `build/` folder so the consuming host emits module attributes during build.

### Schemata.Application.Complex.Targets

Application Complex target. MSBuild flags: `UseDSLTargets=true`, `UseModularTargets=true`, `UseTenancy=true`, `UseAuthorization=true`, `UseIdentity=true`, `UseMapster=true`, `UseRepository=true`, `UseResourceGrpc=true`, `UseResourceHttp=true`, `UseSecurity=true`, `UseValidation=true`.

Effective references, all of base plus:

- `Schemata.Entity.Repository`
- `Schemata.Modular`
- `Schemata.Tenancy.Foundation`
- `Schemata.Authorization.Foundation`
- `Schemata.Identity.Foundation`
- `Schemata.Mapping.Mapster`
- `Schemata.Resource.Foundation`
- `Schemata.Resource.Grpc`
- `Schemata.Resource.Http`
- `Schemata.Security.Foundation`
- `Schemata.Validation.FluentValidation`
- `Schemata.Modeling.Generator` (analyzer)

## Business targets

Business targets reference `Schemata.Abstractions`. Use them for class libraries that define domain models, contracts, and business logic without ASP.NET Core hosting dependencies.

### Schemata.Business.Targets

Base business target. MSBuild flags: none beyond defaults.

Effective references:

- `Schemata.Abstractions`
- `Schemata.Advice.Generator` (analyzer)

### Schemata.Business.Persisting.Targets

Adds the repository pattern. MSBuild flags: `UseRepository=true`.

Adds over base:

- `Schemata.Entity.Repository`

### Schemata.Business.Complex.Targets

Business Complex target. MSBuild flags: `UseDSLTargets=true`, `UseAuthorization=true`, `UseIdentity=true`, `UseMapping=true`, `UseRepository=true`, `UseSecurity=true`.

Effective references, all of base plus:

- `Schemata.Entity.Repository`
- `Schemata.Authorization.Skeleton`
- `Schemata.Identity.Skeleton`
- `Schemata.Mapping.Skeleton`
- `Schemata.Security.Skeleton`
- `Schemata.Modeling.Generator` (analyzer)

## Module targets

Module targets reference `Schemata.Abstractions` and include `Microsoft.AspNetCore.App`. Use them for projects that implement `IModule` and are loaded by `Schemata.Modular` at runtime.

### Schemata.Module.Targets

Base module target. MSBuild flags: none beyond defaults.

Effective references:

- `Schemata.Abstractions`
- `Schemata.Advice.Generator` (analyzer)

Also packs `Schemata.Module.Targets.props`, `Schemata.Module.Targets.targets`, and `Package.Build.props` into the package `build/` folder.

### Schemata.Module.Persisting.Targets

Adds the repository pattern. MSBuild flags: `UseRepository=true`.

Adds over base:

- `Schemata.Entity.Repository`

### Schemata.Module.Complex.Targets

Module Complex target. MSBuild flags: `UseDSLTargets=true`, `UseAuthorization=true`, `UseIdentity=true`, `UseMapping=true`, `UseRepository=true`, `UseSecurity=true`, `UseValidation=true`.

Effective references, all of base plus:

- `Schemata.Entity.Repository`
- `Schemata.Authorization.Skeleton`
- `Schemata.Identity.Skeleton`
- `Schemata.Mapping.Skeleton`
- `Schemata.Security.Skeleton`
- `Schemata.Validation.FluentValidation`
- `Schemata.Modeling.Generator` (analyzer)

## Comparison matrix

| Package | App | App.P | App.M | App.C | Biz | Biz.P | Biz.C | Mod | Mod.P | Mod.C |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Schemata.Core | x | x | x | x | | | | | | |
| Schemata.Abstractions | | | | | x | x | x | x | x | x |
| Schemata.Advice.Generator | x | x | x | x | x | x | x | x | x | x |
| Schemata.Entity.Repository | | x | | x | | x | x | | x | x |
| Schemata.Modular | | | x | x | | | | | | |
| Schemata.Tenancy.Foundation | | | | x | | | | | | |
| Schemata.Authorization.Foundation | | | | x | | | | | | |
| Schemata.Authorization.Skeleton | | | | | | | x | | | x |
| Schemata.Identity.Foundation | | | | x | | | | | | |
| Schemata.Identity.Skeleton | | | | | | | x | | | x |
| Schemata.Mapping.Mapster | | | | x | | | | | | |
| Schemata.Mapping.Skeleton | | | | | | | x | | | x |
| Schemata.Resource.Foundation | | | | x | | | | | | |
| Schemata.Resource.Grpc | | | | x | | | | | | |
| Schemata.Resource.Http | | | | x | | | | | | |
| Schemata.Security.Foundation | | | | x | | | | | | |
| Schemata.Security.Skeleton | | | | | | | x | | | x |
| Schemata.Validation.FluentValidation | | | | x | | | | | | x |
| Schemata.Modeling.Generator | | | | x | | | x | | | x |

**Legend:** App = Application, Biz = Business, Mod = Module, P = Persisting, M = Modular, C = Complex.

## Design motivation

The three-family split reflects the three project roles in a Schemata solution. Application projects host the ASP.NET Core pipeline and need `Schemata.Core` plus `Microsoft.AspNetCore.App`. Business projects define domain logic and need only `Schemata.Abstractions`. Module projects implement `IModule` and need both `Schemata.Abstractions` and `Microsoft.AspNetCore.App` for middleware and endpoint registration.

Within each family, the Persisting variant adds `Schemata.Entity.Repository` for data access. The Complex variant adds the family-specific default set. Business and Module Complex variants reference `*.Skeleton` packages so those projects depend on contracts instead of runtime feature registration.

## Caveats

- `Schemata.Application.Complex.Targets` includes Resource HTTP/gRPC, but it does not include Flow, Scheduling, Push, or Insight foundations. Reference those packages directly in the consuming host when those runtimes are active.
- `Schemata.Business.Complex.Targets` uses `UseMapping=true` and adds `Schemata.Mapping.Skeleton`; it does not add `Schemata.Mapping.Mapster`.
- `UseDSLTargets=true` packs `Schemata.Modeling.Generator` and `Parlot.dll` as analyzers. Build the generators before packing targets so both files exist under `artifacts/bin/Schemata.Modeling.Generator/$(Configuration)/netstandard2.0/`.
- Central package management (`ManagePackageVersionsCentrally=true`) is active. Do not add `Version=` attributes to `PackageReference` elements in consuming projects.

## Optional runtimes not in any meta-target

Reference these packages directly from the host when their runtime is needed:

- `Schemata.Flow.Foundation` — combine with `Schemata.Flow.Http`, `Schemata.Flow.Grpc`, `Schemata.Flow.Event`, or `Schemata.Flow.Scheduling` when the host exposes or bridges process runtime behavior.
- `Schemata.Scheduling.Foundation` — combine with `Schemata.Scheduling.Http`, `Schemata.Scheduling.Grpc`, or `Schemata.Scheduling.Event` when jobs need transport or event surfaces.
- `Schemata.Push.Foundation` — combine with transport implementations and the Resource runtime for subscription storage; see the [Push overview](push/overview.md).
- `Schemata.Push.Scheduling` — combine with `Schemata.Push.Foundation` and `Schemata.Scheduling.Foundation` when deferred sends must return long-running operations; see the [Push overview](push/overview.md).
- `Schemata.Insight.Foundation` — combine with Resource HTTP/gRPC and a repository provider when the host records insight data; see the [Insight overview](insight/overview.md).
- `Schemata.Insight.Http` — combine with `Schemata.Insight.Foundation` and Resource HTTP to expose insight endpoints; see the [Insight overview](insight/overview.md).
- `Schemata.Insight.Grpc` — combine with `Schemata.Insight.Foundation` and Resource gRPC to expose insight endpoints; see the [Insight overview](insight/overview.md).
- `Schemata.Expressions.Order` — combine with Resource or repository code that accepts AIP order-by expressions.

## See also

- [Built-in Features](core/built-in-features.md) — the feature priority table
- [Modules](modules.md) — `IModule`, `ModuleAttribute`, `UseModular()`
- [Getting Started](../guides/getting-started.md) — using `Schemata.Application.Complex.Targets`
