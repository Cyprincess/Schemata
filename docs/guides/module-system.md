# Module System

This guide extracts the Student entity, DbContext, and advisor into a self-contained module. After completing it, the student feature will be discovered and loaded automatically at startup, with its own lifecycle hooks for service registration, middleware configuration, and endpoint mapping.

## Add the modular package

`Schemata.Application.Complex.Targets` already includes `Schemata.Modular`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Modular
```

## Enable modular architecture

Add `UseModular()` to the Schemata builder in `Program.cs`:

```csharp
schema.UseModular();
```

`UseModular()` registers the `DefaultModulesProvider` and `DefaultModulesRunner`. The provider scans the entry assembly for `[Module]` attributes to discover module assemblies. The runner orchestrates three lifecycle phases on each discovered module:

1. `ConfigureServices` -- register services into the DI container
2. `ConfigureApplication` -- configure middleware in the request pipeline
3. `ConfigureEndpoints` -- register route endpoints

## Create the module project

Create a separate class library for the student module:

```shell
dotnet new classlib -n StudentModule
dotnet add StudentModule package --prerelease Schemata.Abstractions
dotnet add StudentModule package --prerelease Schemata.Application.Complex.Targets
```

## Create the module class

Move the Student entity, `AppDbContext`, and `StudentIdAdvisor` into the `StudentModule` project. Then create a module entry point by subclassing `ModuleBase`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using Schemata.Abstractions.Modular;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;

public class StudentModule : ModuleBase
{
    public override int Order => 100;

    public void ConfigureServices(
        IServiceCollection  services,
        IConfiguration      configuration,
        IWebHostEnvironment environment)
    {
        services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
            .UseEntityFrameworkCore<AppDbContext>(
                (_, opts) => opts.UseSqlite("Data Source=app.db"));

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
    }
}
```

`ModuleBase` implements the `IModule` interface, which extends `IFeature`. It provides two ordering properties:

| Property   | Purpose                                                                      | Default         |
| ---------- | ---------------------------------------------------------------------------- | --------------- |
| `Order`    | Controls the sequence during `ConfigureServices`                             | `0`             |
| `Priority` | Controls the sequence during `ConfigureApplication` and `ConfigureEndpoints` | Same as `Order` |

The runner sorts modules by `Order` during service registration and by `Priority` during application and endpoint configuration. Override these properties to control when your module initializes relative to others.

## Register the module

Add a `[Module]` attribute to the host application's `Program.cs` (or any file in the entry assembly) pointing to the module assembly name:

```csharp
using Schemata.Abstractions.Modular;

[assembly: Module("StudentModule")]
```

The `DefaultModulesProvider` reads these assembly-level attributes at startup, loads each named assembly, and finds the first non-abstract class that implements `IModule`. That type becomes the module's entry point.

## Update Program.cs

With the student feature extracted into a module, `Program.cs` becomes minimal. Remove the student-specific service registrations and resource configuration -- the module handles those now:

```csharp
using Schemata.Abstractions.Modular;

[assembly: Module("StudentModule")]

var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseMapster();
        schema.UseModular();

        schema.UseResource()
              .MapHttp()
              .Use<Student, Student, Student, Student>();
    });

var app = builder.Build();
app.Run();
```

## Module lifecycle

The `DefaultModulesRunner` drives each phase by reflecting over the module instance. Methods are optional -- if a module does not declare a lifecycle method, the runner skips it:

| Phase       | Method Signature                                                                  | When                          |
| ----------- | --------------------------------------------------------------------------------- | ----------------------------- |
| Services    | `void ConfigureServices(IServiceCollection, IConfiguration, IWebHostEnvironment)` | During host build             |
| Application | `void ConfigureApplication(IApplicationBuilder)`                                  | After services are built      |
| Endpoints   | `void ConfigureEndpoints(IEndpointRouteBuilder, IApplicationBuilder)`             | During endpoint routing setup |

Module instances are created once during `ConfigureServices`, registered as singletons, and reused for later phases.

## Custom discovery

To load modules from a directory or plugin folder instead of assembly attributes, implement `IModulesProvider`:

```csharp
public class PluginModulesProvider : IModulesProvider
{
    public IEnumerable<ModuleDescriptor> GetModules()
    {
        // Scan a plugins directory, load assemblies, build descriptors
    }
}
```

Register it with the generic overload:

```csharp
schema.UseModular<DefaultModulesRunner, PluginModulesProvider>();
```

## Verify

```shell
dotnet run
```

1. The application starts and the `StudentModule` is discovered via the `[Module]` attribute.
2. The runner calls `ConfigureServices` on the module, registering the repository and advisor.
3. All student endpoints (`GET /students`, `POST /students`, etc.) work exactly as before.
4. Adding a second module with a different `Order` value confirms the loading sequence.

## Next steps

- For the full API surface, custom runners, and provider architecture, see [Modules](../documents/modules.md)
