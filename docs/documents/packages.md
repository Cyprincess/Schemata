# Packages

Schemata ships three families of meta-package targets — Application, Business, and Module — each with three variants (base, Persisting, Complex). Each `.csproj` sets MSBuild property flags; a shared `Directory.Build.props` in each family's folder translates those flags into concrete `ProjectReference` entries. All targets produce `net8.0;net10.0` binaries and bundle `Schemata.Advice.Generator` as an analyzer.

## Where the code lives

| Folder | Files |
| --- | --- |
| `targets/Schemata.Application.Targets/` | `Schemata.Application.Targets.csproj`, `Schemata.Application.Persisting.Targets.csproj`, `Schemata.Application.Modular.Targets.csproj`, `Schemata.Application.Complex.Targets.csproj`, `Directory.Build.props` |
| `targets/Schemata.Business.Targets/` | `Schemata.Business.Targets.csproj`, `Schemata.Business.Persisting.Targets.csproj`, `Schemata.Business.Complex.Targets.csproj`, `Directory.Build.props` |
| `targets/Schemata.Module.Targets/` | `Schemata.Module.Targets.csproj`, `Schemata.Module.Persisting.Targets.csproj`, `Schemata.Module.Complex.Targets.csproj`, `Directory.Build.props` |

## MSBuild flag reference

Each `.csproj` sets one or more of these flags. The `Directory.Build.props` in the same folder translates each flag into a `ProjectReference`:

| Flag | Package added |
| --- | --- |
| `UseRepository=true` | `Schemata.Entity.Repository` |
| `UseModularTargets=true` | `Schemata.Modular` (Application only) |
| `UseTenancy=true` | `Schemata.Tenancy.Foundation` |
| `UseAuthorization=true` | `Schemata.Authorization.Foundation` (Application) or `Schemata.Authorization.Skeleton` (Business/Module) |
| `UseIdentity=true` | `Schemata.Identity.Foundation` (Application) or `Schemata.Identity.Skeleton` (Business/Module) |
| `UseMapster=true` | `Schemata.Mapping.Mapster` |
| `UseMapping=true` | `Schemata.Mapping.Skeleton` |
| `UseResourceGrpc=true` | `Schemata.Resource.Foundation` + `Schemata.Resource.Grpc` |
| `UseResourceHttp=true` | `Schemata.Resource.Foundation` + `Schemata.Resource.Http` |
| `UseSecurity=true` | `Schemata.Security.Foundation` (Application) or `Schemata.Security.Skeleton` (Business/Module) |
| `UseValidation=true` | `Schemata.Validation.FluentValidation` |
| `UseDSLTargets=true` | `Schemata.Modeling.Generator` (packed as analyzer) |

## Application targets

Application targets reference `Schemata.Core` and include `Microsoft.AspNetCore.App`. Use them for host application projects (the entry point that calls `WebApplication.CreateBuilder`).

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

Adds modular architecture. MSBuild flags: `UseModularTargets=true`.

Adds over base:
- `Schemata.Modular`

Also packs `Schemata.Application.Modular.Targets.targets` into the NuGet `build/` folder so the consuming project gets the modular MSBuild targets automatically.

### Schemata.Application.Complex.Targets

All-in-one application target. MSBuild flags: `UseDSLTargets=true`, `UseModularTargets=true`, `UseTenancy=true`, `UseAuthorization=true`, `UseIdentity=true`, `UseMapster=true`, `UseRepository=true`, `UseResourceGrpc=true`, `UseResourceHttp=true`, `UseSecurity=true`, `UseValidation=true`.

Effective references (all of base plus):
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

Business targets reference `Schemata.Abstractions` only (no `Schemata.Core`, no `Microsoft.AspNetCore.App`). Use them for class library projects that define domain models, repository contracts, and business logic without a dependency on ASP.NET Core.

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

All-in-one business target. MSBuild flags: `UseDSLTargets=true`, `UseAuthorization=true`, `UseIdentity=true`, `UseMapping=true`, `UseRepository=true`, `UseSecurity=true`.

Effective references (all of base plus):
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

Also packs `Schemata.Module.Targets.props`, `Schemata.Module.Targets.targets`, and `Package.Build.props` into the NuGet `build/` folder.

### Schemata.Module.Persisting.Targets

Adds the repository pattern. MSBuild flags: `UseRepository=true`.

Adds over base:
- `Schemata.Entity.Repository`

### Schemata.Module.Complex.Targets

All-in-one module target. MSBuild flags: `UseDSLTargets=true`, `UseAuthorization=true`, `UseIdentity=true`, `UseMapping=true`, `UseRepository=true`, `UseSecurity=true`, `UseValidation=true`.

Effective references (all of base plus):
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

**Legend:** App = Application, Biz = Business, Mod = Module, P = Persisting, M = Modular, C = Complex

## Design motivation

The three-family split (Application / Business / Module) reflects the three project roles in a Schemata solution. Application projects host the ASP.NET Core pipeline and need `Schemata.Core` plus `Microsoft.AspNetCore.App`. Business projects define domain logic and need only `Schemata.Abstractions`. Module projects implement `IModule` and need both `Schemata.Abstractions` and `Microsoft.AspNetCore.App` (for middleware and endpoint registration).

Within each family, the Persisting variant adds `Schemata.Entity.Repository` for data access. The Complex variant adds the full suite of runtime packages. Business and Module Complex variants reference `*.Skeleton` packages (contracts only) so the projects stay free of the ASP.NET Core hosting infrastructure that `*.Foundation` packages pull in.

## Caveats

- `Schemata.Application.Complex.Targets` does not include `Schemata.Flow.Foundation` or `Schemata.Scheduling.Foundation`. Add those packages directly if you need them.
- `Schemata.Business.Complex.Targets` uses `UseMapping=true` (adds `Schemata.Mapping.Skeleton`), not `UseMapster=true`. Mapster is an application-layer concern.
- The `UseDSLTargets=true` flag packs `Schemata.Modeling.Generator` and its `Parlot.dll` dependency as analyzers. Both DLLs must exist in `artifacts/bin/Schemata.Modeling.Generator/$(Configuration)/netstandard2.0/` before the Targets pack runs. Build the generators first.
- Central package management (`ManagePackageVersionsCentrally=true`) is active. Do not add `Version=` attributes to `PackageReference` elements in consuming projects.

## See also

- [Built-in Features](core/built-in-features.md) — the feature priority table
- [Modules](modules.md) — `IModule`, `ModuleAttribute`, `UseModular()`
- [Getting Started](../guides/getting-started.md) — using `Schemata.Application.Complex.Targets`
