# Modules

`Schemata.Modular` is the runtime that loads and orchestrates application modules. Modules are themselves declared by package and project references — `Schemata.Application.Modular.Targets.targets` emits the `[assembly: ModuleAttribute("<name>")]` markers on the host during build, so application authors never write the attribute by hand. The runtime feature, `SchemataModulesFeature`, runs at priority 500,000,000 and drives a three-phase lifecycle on every discovered module.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Abstractions` | `Modular/IModule.cs`, `Modular/ModuleBase.cs`, `Modular/ModuleAttribute.cs`, `Modular/ModuleDescriptor.cs` |
| `Schemata.Modular` | `Extensions/SchemataBuilderExtensions.cs` (three `UseModular` overloads), `Features/SchemataModulesFeature.cs`, `DefaultModulesProvider.cs`, `DefaultModulesRunner.cs`, `IModulesProvider.cs`, `IModulesRunner.cs` |
| `targets/Schemata.Application.Targets` | `Schemata.Application.Modular.Targets.targets` — emits the discovery attributes during host build |
| `targets/Schemata.Module.Targets` | `Schemata.Module.Targets.targets` (project-side `GetModuleProjectName` target), `Package.Build.props` (NuGet-side `ModulePackageNames` contribution) |

## Build-time wiring

Module discovery is a build-time concern. There are two roles:

1. **Module project.** Adds one of the `Schemata.Module.*.Targets` packages. That package packs `build/Package.Build.props` (which adds `ModulePackageNames Include="<package-name>"`) and `build/<package>.targets` (which exposes `GetModuleProjectName`, returning `$(AssemblyName)`).
2. **Host project.** Adds one of the `Schemata.Application.*.Targets` packages with `UseModularTargets=true`. That package adds a `<ProjectReference>` to `Schemata.Modular` and packs `Schemata.Application.Modular.Targets.targets`.

During the host build, the packed target runs `AfterResolveReferences`:

```xml
<Target Name="ResolveModuleProjectReferences" AfterTargets="AfterResolveReferences">
  <MSBuild Targets="GetModuleProjectName"
           Projects="@(_MSBuildProjectReferenceExistent)"
           SkipNonexistentTargets="true"
           ContinueOnError="true">
    <Output ItemName="ModuleProjectNames" TaskParameter="TargetOutputs" />
  </MSBuild>

  <ItemGroup>
    <ModuleNames Include="@(ModulePackageNames);@(ModuleProjectNames)" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyAttribute Include="Schemata.Abstractions.Modular.ModuleAttribute"
                       Condition="'@(ModuleNames)' != ''">
      <_Parameter1>%(ModuleNames.Identity)</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
</Target>
```

The result is one `[assembly: Schemata.Abstractions.Modular.ModuleAttribute("<name>")]` per discovered module, emitted into the generated assembly-info source. `<name>` is `$(AssemblyName)` for project references and `$(MSBuildThisFileName)` for packaged modules — in both cases the value is the assembly name the runtime will pass to `Assembly.Load`.

A bare `Schemata.Application.Targets` or `Schemata.Application.Persisting.Targets` host package does not set `UseModularTargets=true`, so the emission step is skipped and no module attributes appear in the host assembly.

## Runtime sequence

`UseModular()` adds `SchemataModulesFeature<TProvider, TRunner>` at `Priority = SchemataConstants.Orders.Extension + 120_000_000` (520,000,000). The three lifecycle phases are:

**ConfigureServices.**
1. `Utilities.CreateInstance<IModulesProvider>(TProvider, logger, configuration, environment)` constructs the provider.
2. `provider.GetModules()` returns module descriptors. The default implementation scans the entry assembly for `ModuleAttribute` instances, calls `Assembly.Load(name)` for each, finds the first non-abstract type implementing `IModule`, harvests assembly metadata (`AssemblyProductAttribute`, `AssemblyDescriptionAttribute`, `AssemblyCompanyAttribute`, `AssemblyCopyrightAttribute`, `AssemblyInformationalVersionAttribute`), and adds a `ModuleDescriptor` to a process-wide `ConcurrentBag`.
3. The result is stored on `SchemataOptions` through `SetModules`.
4. `Utilities.CreateInstance<IModulesRunner>(TRunner, logger, schemata, configuration, environment)` constructs the runner.
5. `runner.ConfigureServices(services, configuration, environment)` runs. The default runner instantiates each module via `Utilities.CreateInstance` (so module constructors may take DI-resolved parameters), sorts the list by `Order`, registers each instance as a singleton (both as the concrete type and as `IModule`), then calls each module's `ConfigureServices` through reflection (`Utilities.CallMethod`).
6. The runner is registered as a singleton `IModulesRunner`.

**ConfigureApplication.** Resolves the runner from DI, which sorts the in-memory module list by `Priority` and calls each module's `ConfigureApplication` via reflection.

**ConfigureEndpoints.** Same flow as `ConfigureApplication`, against the module's `ConfigureEndpoints` method.

Late-arriving features added during another feature's `ConfigureServices` are only invoked for the lifecycle phases that have not yet started; module additions during `ConfigureServices` are picked up only if they entered the dictionary before the sorted snapshot.

## Writing a module

A typical module inherits `ModuleBase` and overrides whichever lifecycle methods it needs:

```csharp
// In MyCompany.MyModule
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Modular;

public sealed class MyModule : ModuleBase
{
    public override int Order => 100;

    public override void ConfigureServices(
        IServiceCollection  services,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddScoped<IMyService, MyService>();
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseMiddleware<MyMiddleware>();
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        endpoints.MapGet("/my-module/health", () => "ok");
    }
}
```

`ModuleBase` defaults to `Order = 0` and `Priority = Order`. Implement `IModule` directly when the two axes must differ.

The host project then adds the module as a `<ProjectReference>` or `<PackageReference>`. No `[assembly: Module("MyCompany.MyModule")]` is needed in the host source — the build target emits it automatically.

## Extension points

| Interface | Purpose |
| --- | --- |
| `IModulesProvider` | Replace `DefaultModulesProvider` to discover modules from a non-attribute source (database, plugin directory, custom configuration). |
| `IModulesRunner` | Replace `DefaultModulesRunner` to customise how lifecycle methods are invoked. |
| `IModule` | Implement directly for full control over `Order` and `Priority`; use `ModuleBase` for the common case where they match. |

`UseModular` exposes three overloads to cover the combinations:

```csharp
builder.UseSchemata(schema => schema.UseModular());                          // default provider + runner
schema.UseModular<MyRunner>();                                               // custom runner, default provider
schema.UseModular<MyRunner, MyProvider>();                                   // both custom
```

## Design motivation

Pushing module discovery into MSBuild keeps the host source free of per-module bookkeeping. Adding or removing a module is a single `<ProjectReference>` or `<PackageReference>` change; the host build picks up the new attribute on its own, and the rest of the toolchain (the targets matrix, the analyzer pack, the version metadata) follows from the package layer.

`DefaultModulesProvider` resolves module assemblies through `Assembly.Load(name)` rather than `Type.GetType("Namespace.Type, Assembly")` so that authors do not have to expose a specific entry type name. The provider then scans the loaded assembly for the first concrete `IModule` implementation. This indirection allows a module assembly to rename its module class without changing anything in the host's build configuration.

The static `ConcurrentBag` cache inside `DefaultModulesProvider` prevents repeated assembly scanning when the provider is instantiated multiple times in the same process (typical inside integration test hosts).

## Caveats

- `[Module]` attributes appearing in the host source are tolerated (`AllowMultiple = true`), but they bypass the build-time emission path and are easy to drift from the actual reference graph. Author modules through `<ProjectReference>` or `<PackageReference>` only.
- `Schemata.Application.Targets` and `Schemata.Application.Persisting.Targets` do not enable `UseModularTargets`. Hosts that need modular discovery must reference `Schemata.Application.Modular.Targets` or `Schemata.Application.Complex.Targets`.
- `DefaultModulesProvider` caches descriptors in a static `ConcurrentBag`. Multiple test hosts that share the same process see the same cache; use a custom `IModulesProvider` in tests when isolation matters.
- Module lifecycle methods are invoked via reflection through `Utilities.CallMethod`. A method whose name does not match one of the three lifecycle hooks is silently ignored — there is no compile-time enforcement.
- `DefaultModulesRunner.ConfigureServices` sorts modules by `Order`; `ConfigureApplication` and `ConfigureEndpoints` sort by `Priority`. These are independent axes matching the same `Order` / `Priority` split used by features.
- `ModuleAttribute.Name` is the assembly name `Assembly.Load` will use. The XML doc on the attribute uses the phrase "fully-qualified type name", which predates the build-time emission path and is misleading; the code itself uses `Assembly.Load`, so an assembly name is what flows in at runtime.

## See also

- [Built-in Features](core/built-in-features.md) — full feature priority table
- [Feature System](core/feature-system.md) — `Order` vs `Priority` semantics
- [Packages](packages.md) — `Schemata.Application.*.Targets` and `Schemata.Module.*.Targets` matrix
- [Modular guide](../guides/modular.md) — extracting an entity into a module
- [Module Packaging](../cookbook/module-packaging.md) — packaging a module for downstream hosts
