# Modules

`Schemata.Modular` loads and orchestrates application modules. A module is wired in by a package or
project reference: `Schemata.Application.Modular.Targets.targets` stamps an
`[assembly: ModuleAttribute("<name>")]` onto the host during build, so application authors never
write the attribute by hand. The runtime feature, `SchemataModulesFeature<TProvider, TRunner>`,
runs at Priority 520,000,000 and drives a three-phase lifecycle on every discovered module.

## Where the code lives

| Package                                | Key files                                                                                                                                                                                                                                 |
| -------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Abstractions`                | `Modular/IModule.cs`, `Modular/ModuleBase.cs`, `Modular/ModuleAttribute.cs`                                                                                                                                                               |
| `Schemata.Modular`                     | `Extensions/SchemataBuilderExtensions.cs` (three `UseModular` overloads), `Features/SchemataModulesFeature.cs`, `DefaultModulesProvider.cs`, `DefaultModulesRunner.cs`, `ModuleDescriptor.cs`, `IModulesProvider.cs`, `IModulesRunner.cs` |
| `targets/Schemata.Application.Targets` | `Schemata.Application.Modular.Targets.targets` — stamps the discovery attributes                                                                                                                                                          |
| `targets/Schemata.Module.Targets`      | `Schemata.Module.Targets.targets` (`GetModuleProjectName`), `Package.Build.props` (`ModulePackageNames`)                                                                                                                                  |

## IModule and ModuleBase

`Schemata.Abstractions.Modular.IModule` extends `IFeature`, so it carries `Order` and `Priority`.
`ModuleBase` defaults `Order` to 0 and `Priority` to `Order`. The lifecycle methods mirror a
feature's:

```csharp
public abstract class ModuleBase : IModule
{
    public virtual int Order    => 0;
    public virtual int Priority => Order;
    // ConfigureServices / ConfigureApplication / ConfigureEndpoints overrides
}
```

Implement `IModule` directly only when `Order` and `Priority` must differ.

## Build-time wiring

Module discovery is a build-time concern with two roles:

1. **Module project.** References one of the `Schemata.Module.*.Targets` packages. That package
   packs `build/Package.Build.props` (adding `ModulePackageNames Include="<package-name>"`) and
   `build/<package>.targets` (exposing `GetModuleProjectName`, which returns `$(AssemblyName)`).
2. **Host project.** References an `Schemata.Application.*.Targets` package with
   `UseModularTargets=true`. That package adds a `ProjectReference` to `Schemata.Modular` and packs
   `Schemata.Application.Modular.Targets.targets`.

During the host build, the packed target runs after `AfterResolveReferences`:

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

The result is one `[assembly: Schemata.Abstractions.Modular.ModuleAttribute("<name>")]` per
discovered module in the generated assembly-info source. `<name>` is `$(AssemblyName)` for project
references and `$(MSBuildThisFileName)` for packaged modules — in both cases the assembly name the
runtime passes to `Assembly.Load`.

A bare `Schemata.Application.Targets` or `Schemata.Application.Persisting.Targets` host package does
not set `UseModularTargets=true`, so no module attributes are stamped.

## Runtime sequence

`UseModular()` adds `SchemataModulesFeature<DefaultModulesProvider, DefaultModulesRunner>` at
`Priority = SchemataConstants.Orders.Extension + 120_000_000` (520,000,000).

**ConfigureServices.**

1. `Utilities.CreateInstance<IModulesProvider>(TProvider, logger, configuration, environment)`
   builds the provider.
2. `provider.GetModules()` returns descriptors. `DefaultModulesProvider` scans the entry assembly
   for `ModuleAttribute` instances, calls `Assembly.Load(name)` for each, finds the first
   non-abstract `IModule` type, harvests assembly metadata
   (`AssemblyProductAttribute` → `DisplayName`, `AssemblyDescriptionAttribute`,
   `AssemblyCompanyAttribute`, `AssemblyCopyrightAttribute`, `AssemblyInformationalVersionAttribute`,
   falling back to `AssemblyVersionAttribute`), and adds a `ModuleDescriptor` to a static
   `ConcurrentBag`.
3. The descriptors are stored on `SchemataOptions` via `SetModules`.
4. `Utilities.CreateInstance<IModulesRunner>(TRunner, logger, schemata, configuration, environment)`
   builds the runner.
5. `runner.ConfigureServices(...)` instantiates each module via `Utilities.CreateInstance`, sorts by
   `Order`, registers each as a singleton (concrete type and `IModule`), then invokes each module's
   `ConfigureServices` by reflection (`Utilities.CallMethod`) when the method exists.
6. The runner registers as a singleton `IModulesRunner`.

**ConfigureApplication / ConfigureEndpoints.** Resolve the registered `IModule` instances, sort by
`Priority`, and invoke the corresponding lifecycle method by reflection when it exists.

## Writing a module

A module inherits `ModuleBase` and overrides the lifecycle methods it needs:

```csharp
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
        IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment
    ) {
        services.AddScoped<IMyService, MyService>();
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder app, IEndpointRouteBuilder endpoints,
        IConfiguration configuration, IWebHostEnvironment environment
    ) {
        endpoints.MapGet("/my-module/health", () => "ok");
    }
}
```

The host then references the module as a `ProjectReference` or `PackageReference`. No
`[assembly: Module(...)]` in the host source — the build target stamps it.

## Extension points

| Interface          | Purpose                                                                                                                              |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| `IModulesProvider` | Replace `DefaultModulesProvider` to discover modules from a non-attribute source (database, plugin directory, custom configuration). |
| `IModulesRunner`   | Replace `DefaultModulesRunner` to change how lifecycle methods are invoked.                                                          |
| `IModule`          | Implement directly for independent `Order` and `Priority`; use `ModuleBase` when they match.                                         |

`UseModular` has three overloads (runner first, provider second):

```csharp
builder.UseSchemata(schema => schema.UseModular());        // default provider + runner
schema.UseModular<MyRunner>();                             // custom runner, default provider
schema.UseModular<MyRunner, MyProvider>();                 // both custom
```

## Design rationale

Pushing discovery into MSBuild keeps the host source free of per-module bookkeeping: adding or
removing a module is a single reference change, and the build stamps the matching attribute on its
own. `DefaultModulesProvider` resolves module assemblies with `Assembly.Load(name)` and then scans
for the first concrete `IModule`, so a module assembly can rename its module class without touching
the host's build configuration. The static `ConcurrentBag` cache avoids rescanning when the
provider is constructed several times in one process (typical in integration-test hosts).

## Caveats

- `ModuleAttribute.Name` is the assembly name `Assembly.Load` consumes. Its XML doc still reads
  "fully-qualified type name", which predates the build-time stamping path; the code uses
  `Assembly.Load`, so an assembly name is what flows in.
- A hand-authored `[assembly: Module(...)]` in the host is tolerated (`AllowMultiple = true`) but
  bypasses the build-time path and drifts from the reference graph. Author modules through
  references only.
- `DefaultModulesProvider` caches descriptors in a static `ConcurrentBag`. Test hosts sharing a
  process share the cache; supply a custom `IModulesProvider` when test isolation matters.
- A module method whose name does not match one of the three lifecycle hooks is never invoked;
  there is no compile-time check.

## See also

- [Modular guide](../guides/modular.md) — extracting an entity into a module
- [Packages](packages.md) — the `Schemata.Application.*.Targets` / `Schemata.Module.*.Targets` matrix
- [Module Packaging](../cookbook/module-packaging.md) — packaging a module for downstream hosts
