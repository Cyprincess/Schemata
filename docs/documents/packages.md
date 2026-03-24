# Packages

Schemata provides meta-package targets that bundle related packages for different project types. Each target tier adds capabilities on top of the base.

The targets use MSBuild property flags in their `.csproj` files. A shared `Directory.Build.props` translates these flags into concrete project references. The table below shows the effective package set for each target.

## Application targets

Application targets reference `Schemata.Core` and target `net8.0;net10.0` with `Microsoft.AspNetCore.App`.

### Schemata.Application.Targets

The base application target. Includes:

- `Schemata.Core`
- `Schemata.Advice.Generator` (source generator, packed as analyzer)

### Schemata.Application.Persisting.Targets

Adds the repository pattern. Includes everything in the base target plus:

- `Schemata.Entity.Repository`

### Schemata.Application.Modular.Targets

Adds modular architecture support. Includes everything in the base target plus:

- `Schemata.Modular`

### Schemata.Application.Complex.Targets

The comprehensive application target. Includes everything in the base target plus:

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
- `Schemata.Workflow.Foundation`
- `Schemata.Modeling.Generator` (source generator, packed as analyzer)

## Business targets

Business targets reference `Schemata.Abstractions` and target `netstandard2.0;netstandard2.1`, making them suitable for shared business logic libraries that do not depend on ASP.NET Core.

### Schemata.Business.Targets

The base business target. Includes:

- `Schemata.Abstractions`
- `Schemata.Advice.Generator` (source generator, packed as analyzer)

### Schemata.Business.Persisting.Targets

Adds the repository pattern. Includes everything in the base target plus:

- `Schemata.Entity.Repository`

### Schemata.Business.Complex.Targets

The comprehensive business target. Includes everything in the base target plus:

- `Schemata.Entity.Repository`
- `Schemata.Authorization.Skeleton`
- `Schemata.Identity.Skeleton`
- `Schemata.Mapping.Skeleton`
- `Schemata.Security.Skeleton`
- `Schemata.Workflow.Skeleton`
- `Schemata.Modeling.Generator` (source generator, packed as analyzer)

## Module targets

Module targets reference `Schemata.Abstractions` and target `net8.0;net10.0` with `Microsoft.AspNetCore.App`. They are designed for projects that implement `IModule` and are loaded by the modular system.

### Schemata.Module.Targets

The base module target. Includes:

- `Schemata.Abstractions`
- `Schemata.Advice.Generator` (source generator, packed as analyzer)

### Schemata.Module.Persisting.Targets

Adds the repository pattern. Includes everything in the base target plus:

- `Schemata.Entity.Repository`

### Schemata.Module.Complex.Targets

The comprehensive module target. Includes everything in the base target plus:

- `Schemata.Entity.Repository`
- `Schemata.Authorization.Skeleton`
- `Schemata.Identity.Skeleton`
- `Schemata.Mapping.Skeleton`
- `Schemata.Security.Skeleton`
- `Schemata.Validation.FluentValidation`
- `Schemata.Workflow.Skeleton`
- `Schemata.Modeling.Generator` (source generator, packed as analyzer)

## Target comparison matrix

| Package | App | App.P | App.M | App.C | Biz | Biz.P | Biz.C | Mod | Mod.P | Mod.C |
|---|---|---|---|---|---|---|---|---|---|---|
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
| Schemata.Workflow.Foundation | | | | x | | | | | | |
| Schemata.Workflow.Skeleton | | | | | | | x | | | x |
| Schemata.Modeling.Generator | | | | x | | | x | | | x |

**Legend:** App = Application, Biz = Business, Mod = Module, P = Persisting, M = Modular, C = Complex
