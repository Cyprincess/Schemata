# Module Packaging

## What you'll build

A standalone `Catalog` module that the host application picks up through package or project references — no hand-written `[Module]` attribute anywhere. You'll see which targets package each side uses, how MSBuild emits the discovery attributes, and how `DefaultModulesProvider` consumes them at runtime.

## Prerequisites

- A working host application from [Getting Started](../guides/getting-started.md) that references `Schemata.Application.Complex.Targets` (or any other Application Targets variant with `UseModularTargets=true`).
- A `dotnet` SDK that satisfies `global.json` (currently 10.0.201).

## Step 1: Create the module project

```shell
dotnet new classlib -n MyApp.Catalog
dotnet add MyApp.Catalog package --prerelease Schemata.Module.Complex.Targets
```

The module-side targets variants:

| Package | Purpose |
| --- | --- |
| `Schemata.Module.Targets` | Minimal: `Schemata.Abstractions` + Advice generator |
| `Schemata.Module.Persisting.Targets` | Base + `Schemata.Entity.Repository` |
| `Schemata.Module.Complex.Targets` | Persisting + DSL + Authorization/Identity/Mapping/Security/Validation Skeletons |

The Targets package packs `build/Package.Build.props` (which contributes `ModulePackageNames Include="<package-name>"`) and `build/<package>.targets` (which exposes a `GetModuleProjectName` target). Consuming hosts use those to discover the module at build time.

**Assertion:** `dotnet build MyApp.Catalog` succeeds. The module project compiles against `Schemata.Abstractions` and ships an analyzer reference to `Schemata.Advice.Generator`.

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

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        // Middleware specific to this module, if any.
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

`ModuleBase` defaults to `Order = 0` and `Priority = Order`. Override `Order` to control where this module sits among other modules for `ConfigureServices`, and `Priority` to split `ConfigureApplication`/`ConfigureEndpoints` ordering. Implement `IModule` directly when the two axes must differ.

**Assertion:** `typeof(CatalogModule).IsAssignableTo(typeof(IModule))` is `true`. The class compiles inside the module project.

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

That single reference is the only registration step. The host already has `Schemata.Application.Modular.Targets` (or `Schemata.Application.Complex.Targets`, which sets `UseModularTargets=true`). At build time the `ResolveModuleProjectReferences` MSBuild target — packed inside that Application Targets package — runs `AfterResolveReferences`, calls `GetModuleProjectName` against every responding project reference, merges the result with `ModulePackageNames` from referenced module packages, and writes:

```csharp
[assembly: Schemata.Abstractions.Modular.ModuleAttribute("MyApp.Catalog")]
```

into the host assembly. The string is whichever name MSBuild collected — `$(AssemblyName)` for project references, `$(MSBuildThisFileName)` for package references.

**Assertion:** Rebuild the host. The generated `obj/.../*.AssemblyInfo.cs` contains a `Schemata.Abstractions.Modular.ModuleAttribute("MyApp.Catalog")` entry. No hand-written `[Module]` attribute exists in your sources.

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

`UseModular()` registers `SchemataModulesFeature<DefaultModulesProvider, DefaultModulesRunner>` at `Priority = 520_000_000`. At `ConfigureServices` time `DefaultModulesProvider` reads the emitted `ModuleAttribute` instances from `Assembly.GetEntryAssembly()`, calls `Assembly.Load(name)` for each, finds the first non-abstract `IModule` type, and stores a `ModuleDescriptor`. `DefaultModulesRunner` then dispatches each lifecycle phase.

**Assertion:** The host starts. `GET /catalog/health` returns `"ok"`. `ICatalogService` resolves from the DI container.

## Step 5: Provide module metadata

`DefaultModulesProvider` reads standard assembly attributes so tooling and admin UIs can display module information:

```xml
<!-- MyApp.Catalog.csproj -->
<PropertyGroup>
  <Product>Catalog Module</Product>
  <Description>Product catalog management</Description>
  <Company>MyApp Inc.</Company>
  <Version>1.2.3</Version>
</PropertyGroup>
```

These map onto `ModuleDescriptor.Display`, `Description`, `Company`, and `Version`. If the version string contains a `+` build-metadata suffix the descriptor trims it to a stable prefix (first 8 characters for a 40-character commit hash, first 12 otherwise).

**Assertion:** Resolving `IModulesProvider` and calling `GetModules()` returns a descriptor whose `Display` is `"Catalog Module"`.

## Step 6: Plug in a custom provider or runner

For sourcing modules from somewhere other than the entry assembly's emitted attributes, implement `IModulesProvider` and pass it to `UseModular<TRunner, TProvider>`:

```csharp
schema.UseModular<DefaultModulesRunner, DatabaseModulesProvider>();
```

Custom runners customise how lifecycle methods are invoked (for example, to inject per-module configuration), via the same overload.

**Assertion:** The custom provider's `GetModules()` runs at startup. Modules it returns flow through the chosen runner's lifecycle.

## Common pitfalls

- **Module project references the wrong Targets package.** A module project must reference one of the `Schemata.Module.*.Targets` packages. Referencing `Schemata.Application.*.Targets` from a module is wrong — it pulls Application-tier dependencies into a library that should expose only module-level capability.
- **Host project missing `UseModularTargets=true`.** Without it the host's MSBuild build never runs `ResolveModuleProjectReferences`, so no `[Module]` attribute is emitted and the runtime sees zero modules. Both `Schemata.Application.Modular.Targets` and `Schemata.Application.Complex.Targets` enable it; the bare `Schemata.Application.Targets` and `Schemata.Application.Persisting.Targets` do not.
- **Module assembly not on the probing path.** `Assembly.Load(name)` uses the default probing rules. If `MyApp.Catalog.dll` is neither in the application's base directory nor pulled in transitively by a reference, the load fails at startup.
- **Two `IModule` types in one assembly.** `DefaultModulesProvider` picks the first non-abstract type via `FirstOrDefault`; the rest are silently ignored. Put one module class per assembly, or roll a custom `IModulesProvider`.
- **Hand-authored `[assembly: Module(...)]` in the host.** The MSBuild target emits its own attributes during build. Adding another by hand works (the attribute is `AllowMultiple = true`), but it bypasses the package-reference path that the rest of the tooling relies on, so the host gets out of sync with the references it actually has.

## See also

- [Modular guide](../guides/modular.md) — extracting an existing feature into a module
- [Modules](../documents/modules.md) — build-time wiring + runtime discovery internals
- [Packages](../documents/packages.md) — the full Application / Business / Module Targets matrix
