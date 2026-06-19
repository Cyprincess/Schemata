# Module Packaging

## What you'll build

A standalone `Catalog` module that the host application picks up through a package or project
reference — no hand-written `[Module]` attribute anywhere. You'll see which targets package each
side uses, how MSBuild stamps the discovery attributes, and how `DefaultModulesProvider` consumes
them at runtime.

## Prerequisites

- A working host application from [Getting Started](../guides/getting-started.md) that references
  `Schemata.Application.Complex.Targets` (or any Application Targets variant with
  `UseModularTargets=true`).
- A `dotnet` SDK that satisfies `global.json`.

## Step 1: Create the module project

```shell
dotnet new classlib -n MyApp.Catalog
dotnet add MyApp.Catalog package --prerelease Schemata.Module.Complex.Targets
```

The module-side targets variants:

| Package | Adds |
| --- | --- |
| `Schemata.Module.Targets` | `Schemata.Abstractions` + the advice generator |
| `Schemata.Module.Persisting.Targets` | Base + `Schemata.Entity.Repository` |
| `Schemata.Module.Complex.Targets` | Persisting + DSL + Authorization/Identity/Mapping/Security/Validation skeletons |

The Targets package packs `build/Package.Build.props` (contributing
`ModulePackageNames Include="<package-name>"`) and `build/<package>.targets` (exposing
`GetModuleProjectName`). Consuming hosts use those to discover the module at build time.

**Assertion:** `dotnet build MyApp.Catalog` succeeds. The project compiles against
`Schemata.Abstractions` and ships an analyzer reference to `Schemata.Advice.Generator`.

## Step 2: Define the module class

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Modular;

namespace MyApp.Catalog;

public sealed class CatalogModule : ModuleBase
{
    public override int Order => 1_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddScoped<ICatalogService, CatalogService>();
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        endpoints.MapGet("/catalog/health", () => "ok");
    }
}
```

`ModuleBase` defaults `Order` to 0 and `Priority` to `Order`. Override `Order` to position the
module among others for `ConfigureServices`, and `Priority` to split
`ConfigureApplication` / `ConfigureEndpoints` ordering. Implement `IModule` directly when the two
axes must differ.

**Assertion:** `typeof(CatalogModule).IsAssignableTo(typeof(IModule))` is `true`.

## Step 3: Reference the module from the host

If the module is local to the solution, add a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\MyApp.Catalog\MyApp.Catalog.csproj" />
</ItemGroup>
```

If the module is already published, add a package reference:

```xml
<ItemGroup>
  <PackageReference Include="MyApp.Catalog" Version="1.2.3" />
</ItemGroup>
```

That single reference is the only registration step. The host already has
`Schemata.Application.Modular.Targets` (or `Schemata.Application.Complex.Targets`, which sets
`UseModularTargets=true`). At build time the `ResolveModuleProjectReferences` MSBuild target — packed
inside that Application Targets package — runs after `AfterResolveReferences`, calls
`GetModuleProjectName` against every responding project reference, merges the result with
`ModulePackageNames` from referenced module packages, and writes:

```csharp
[assembly: Schemata.Abstractions.Modular.ModuleAttribute("MyApp.Catalog")]
```

into the host assembly. The string is whichever name MSBuild collected — `$(AssemblyName)` for
project references, `$(MSBuildThisFileName)` for package references.

**Assertion:** Rebuild the host. The generated `obj/.../*.AssemblyInfo.cs` contains a
`Schemata.Abstractions.Modular.ModuleAttribute("MyApp.Catalog")` entry, with no hand-written
`[Module]` attribute in your sources.

## Step 4: Enable the modular feature

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseModular();
    });
```

`UseModular()` registers `SchemataModulesFeature<DefaultModulesProvider, DefaultModulesRunner>` at
`Priority = 520_000_000`. During `ConfigureServices`, `DefaultModulesProvider` reads the stamped
`ModuleAttribute` instances from `Assembly.GetEntryAssembly()`, calls `Assembly.Load(name)` for
each, finds the first non-abstract `IModule` type, and stores a `ModuleDescriptor`.
`DefaultModulesRunner` then dispatches each lifecycle phase.

**Assertion:** The host starts. `GET /catalog/health` returns `"ok"`. `ICatalogService` resolves
from the DI container.

## Step 5: Provide module metadata

`DefaultModulesProvider` reads standard assembly attributes so tooling and admin UIs can display
module information:

```xml
<!-- MyApp.Catalog.csproj -->
<PropertyGroup>
  <Product>Catalog Module</Product>
  <Description>Product catalog management</Description>
  <Company>MyApp Inc.</Company>
  <Version>1.2.3</Version>
</PropertyGroup>
```

These map onto `ModuleDescriptor.DisplayName`, `Description`, `Company`, and `Version`. When the
version string carries a `+` build-metadata suffix, the descriptor trims it to a stable prefix —
the first 8 characters of a 40-character commit hash, otherwise the first 12.

**Assertion:** Resolving `IModulesProvider` and calling `GetModules()` returns a descriptor whose
`DisplayName` is `"Catalog Module"`.

## Step 6: Plug in a custom provider or runner

To source modules from somewhere other than the entry assembly's stamped attributes, implement
`IModulesProvider` and pass it to `UseModular<TRunner, TProvider>`:

```csharp
schema.UseModular<DefaultModulesRunner, DatabaseModulesProvider>();
```

A custom runner customizes how lifecycle methods are invoked through the same overload.

**Assertion:** The custom provider's `GetModules()` runs at startup; the modules it returns flow
through the chosen runner's lifecycle.

## Common pitfalls

- **Module references the wrong Targets package.** A module must reference one of the
  `Schemata.Module.*.Targets` packages. Referencing `Schemata.Application.*.Targets` pulls
  Application-tier dependencies into a library that should expose only module-level capability.
- **Host missing `UseModularTargets=true`.** Without it, the host build never runs
  `ResolveModuleProjectReferences`, so no `[Module]` attribute is stamped and the runtime sees zero
  modules. `Schemata.Application.Modular.Targets` and `Schemata.Application.Complex.Targets` enable
  it; the bare `Schemata.Application.Targets` and `Schemata.Application.Persisting.Targets` do not.
- **Module assembly off the probing path.** `Assembly.Load(name)` follows the default probing
  rules. If `MyApp.Catalog.dll` is neither in the application's base directory nor pulled in
  transitively, the load fails at startup.
- **Two `IModule` types in one assembly.** `DefaultModulesProvider` takes the first non-abstract
  type via `FirstOrDefault`; the rest are skipped. Keep one module class per assembly, or supply a
  custom `IModulesProvider`.
- **Hand-authored `[assembly: Module(...)]` in the host.** The MSBuild target stamps its own
  attributes. A hand-written one works (`AllowMultiple = true`) but bypasses the reference path the
  rest of the tooling relies on, so the host drifts from its actual references.

## See also

- [Modular guide](../guides/modular.md) — extracting an existing feature into a module
- [Modules](../documents/modules.md) — build-time wiring and runtime discovery internals
- [Packages](../documents/packages.md) — the full Application / Business / Module Targets matrix
