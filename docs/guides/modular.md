# Modular

Extract the `Student` feature into a self-contained module assembly. The host picks it up through a single package or project reference; the `[Module]` attribute that wires it in is stamped into the host assembly at build time. This guide builds on [Getting Started](getting-started.md).

## How it works

A host references the module as a package or project, and at build time MSBuild stamps a `[assembly: ModuleAttribute("<name>")]` into the host assembly for each discovered module; at runtime `DefaultModulesProvider` reads those attributes, loads the named assemblies, and `DefaultModulesRunner` runs each module's `ConfigureServices`, `ConfigureApplication`, and `ConfigureEndpoints` in `Order`/`Priority` sequence.

The MSBuild side of that hand-off — `GetModuleProjectName`, `ResolveModuleProjectReferences`, `ModulePackageNames` — is detailed in [Modules](../documents/modules.md).

## Enable the modular feature in the host

`Schemata.Application.Complex.Targets` (used by Getting Started) already sets `UseModularTargets=true`, so the host build emits the attributes. The only startup change is to call `UseModular()`:

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseJsonSerializer();
        schema.UseResource()
              .MapHttp()
              .Use<Student>();

        schema.UseModular();
    });

var app = builder.Build();
app.Run();
```

`UseModular()` adds `SchemataModulesFeature<DefaultModulesProvider, DefaultModulesRunner>` at `Priority = 520_000_000`.

## Create the module project

In a sibling directory next to your host app, create a class library and add the right module-side targets package:

```shell
dotnet new classlib -n StudentModule
dotnet add StudentModule package --prerelease Schemata.Module.Complex.Targets
```

The Complex variant is the easiest starting point — it pulls in `Schemata.Abstractions`, the Repository pattern, the Modeling DSL, Authorization/Identity/Security/Validation/Mapping skeletons, and the Advice generator analyzer. Use `Schemata.Module.Targets` or `Schemata.Module.Persisting.Targets` if you want a smaller dependency set.

## Move the entity and its advisor into the module

Move `Student.cs`, `AppDbContext.cs`, and `StudentNameAdvisor.cs` (from Getting Started) into the `StudentModule` project. Then add a module entry point that inherits `ModuleBase`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Modular;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;

namespace StudentModule;

public sealed class StudentModule : ModuleBase
{
    public override int Order => 100;

    public override void ConfigureServices(
        IServiceCollection  services,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
    }
}
```

`ModuleBase` defaults `Order = 0` and `Priority = Order`. Override `Order` to control where this module's `ConfigureServices` lands relative to other modules; override `Priority` for `ConfigureApplication`/`ConfigureEndpoints` ordering. Implement `IModule` directly only when you need the two axes to differ.

## Wire the module into the host

In the host application's `.csproj`, add a project reference (or a published `<PackageReference>` if you ship the module as a NuGet):

```xml
<ItemGroup>
  <ProjectReference Include="..\StudentModule\StudentModule.csproj" />
</ItemGroup>
```

That single line is the whole "registration" step. When you build the host, `Schemata.Application.Modular.Targets.targets` calls `GetModuleProjectName` on `StudentModule.csproj`, gets back `StudentModule` (its `$(AssemblyName)`), and emits `[assembly: Schemata.Abstractions.Modular.ModuleAttribute("StudentModule")]` into the host assembly. NuGet-published modules emit the same attribute via the `Package.Build.props` they pack into the consuming app.

## Trim the host startup

With the data access and the custom advisor extracted into the module, the host startup file only configures cross-cutting features and the resource endpoint:

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseJsonSerializer();
        schema.UseResource()
              .MapHttp()
              .Use<Student>();

        schema.UseModular();
    });

var app = builder.Build();
app.Run();
```

## Verify

```shell
dotnet run
```

The application starts, `StudentModule.ConfigureServices` registers the EF Core repository and the name advisor, and every endpoint from Getting Started keeps working. Open the build log if you want to see the auto-emitted attribute — search for `ModuleAttribute` under the `ResolveModuleProjectReferences` target output, or inspect the generated `AssemblyInfo` source under `obj/`.

## Custom discovery

To load modules from a directory or plugin folder instead of relying on the MSBuild-emitted attributes, implement `IModulesProvider`:

```csharp
public sealed class PluginModulesProvider : IModulesProvider
{
    public IEnumerable<ModuleDescriptor> GetModules() {
        // scan a plugins directory, load assemblies, build descriptors
        yield break;
    }
}
```

Register it with the generic overload:

```csharp
schema.UseModular<DefaultModulesRunner, PluginModulesProvider>();
```

A custom provider replaces `DefaultModulesProvider` entirely — the MSBuild-emitted attributes are ignored unless the custom provider chooses to read them.

## See also

- [Scheduling](scheduling.md) — previous in the series: cron and periodic background jobs
- [Getting Started](getting-started.md) — the `Student` entity and host startup
- [Modules](../documents/modules.md) — build-time wiring, runtime discovery, lifecycle internals
- [Packages](../documents/packages.md) — the full `Schemata.Application.*.Targets` / `Schemata.Module.*.Targets` matrix
- [Module Packaging](../cookbook/module-packaging.md) — packaging a module as a NuGet for downstream hosts
