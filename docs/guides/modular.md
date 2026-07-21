# Modular

Extract the `Student` feature into a self-contained module assembly. The host picks it up through a
single package or project reference; the `[Module]` attribute that wires it in is stamped into the
host assembly at build time. This guide builds on [Getting Started](getting-started.md).

## How it works

A host references the module as a package or project. At build time MSBuild stamps an
`[assembly: ModuleAttribute("<name>")]` into the host assembly for each discovered module. At
runtime `DefaultModulesProvider` reads those attributes, loads the named assemblies, and
`DefaultModulesRunner` runs each module's `ConfigureServices`, `ConfigureApplication`, and
`ConfigureEndpoints` in `Order` / `Priority` sequence.

## Enable the modular feature in the host

`Schemata.Application.Complex.Targets` (used by Getting Started) already sets
`UseModularTargets=true`, so the host build stamps the attributes. The only startup change is to
call `UseModular()`:

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

`UseModular()` activates module discovery with the default provider and runner; the feature
internals are in [Modules](../documents/modules.md).

## Create the module project

In a sibling directory next to the host app, create a class library and add the module-side
targets package:

```shell
dotnet new classlib -n StudentModule
dotnet add StudentModule package --prerelease Schemata.Module.Complex.Targets
```

The Complex variant pulls in `Schemata.Abstractions`, the repository pattern, the modeling DSL, the
Authorization/Identity/Security/Mapping skeletons, `Schemata.Validation.FluentValidation`, and the
advice generator. Use
`Schemata.Module.Targets` or `Schemata.Module.Persisting.Targets` for a smaller dependency set.

## Move the entity and its advisor into the module

Move `Student.cs`, `AppDbContext.cs`, and `AdviceAddStudentName.cs` from Getting Started into the
`StudentModule` project, then add a module entry point that inherits `ModuleBase`:

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
        services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, AdviceAddStudentName>());
    }
}
```

`ModuleBase` defaults `Order` to 0 and `Priority` to `Order`. Override `Order` to position this
module's `ConfigureServices` among other modules; override `Priority` for
`ConfigureApplication` / `ConfigureEndpoints`. Implement `IModule` directly only when the two axes
must differ.

## Wire the module into the host

In the host `.csproj`, add a project reference (or a `PackageReference` if you publish the module):

```xml
<ItemGroup>
  <ProjectReference Include="..\StudentModule\StudentModule.csproj" />
</ItemGroup>
```

That single line is the whole registration step. When the host builds, the packed MSBuild targets
stamp `[assembly: ModuleAttribute("StudentModule")]` into the host assembly; a NuGet-published
module stamps the same attribute through the props file it packs into the consuming app. The
target-by-target build wiring is in [Modules](../documents/modules.md).

## Trim the host startup

With data access and the custom advisor extracted, the host startup configures only the
cross-cutting features and the resource endpoint:

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

The application starts, `StudentModule.ConfigureServices` registers the EF Core repository and the
name advisor, and every Getting Started endpoint keeps working. To see the stamped attribute,
inspect the generated assembly-info source under `obj/`.

## Custom discovery

To load modules from a directory or plugin folder instead of the stamped attributes, implement
`IModulesProvider`:

```csharp
public sealed class PluginModulesProvider : IModulesProvider
{
    public IEnumerable<ModuleDescriptor> GetModules() {
        // scan a plugins directory, load assemblies, build descriptors
        yield break;
    }
}
```

Register it with the generic overload (runner first, provider second):

```csharp
schema.UseModular<DefaultModulesRunner, PluginModulesProvider>();
```

A custom provider replaces `DefaultModulesProvider`; the stamped attributes are read only if the
provider chooses to read them.

## Next steps

- [Multi-Tenancy](multi-tenancy.md) — extracted modules can register per-tenant services
- [Flow](flow.md) — package a BPMN process inside its own module
- [Scheduling](scheduling.md) — register jobs from the module's `ConfigureServices`

## See also

- [Modules](../documents/modules.md) — build-time wiring, runtime discovery, lifecycle internals
- [Module Packaging](../cookbook/module-packaging.md) — packaging a module as a NuGet
