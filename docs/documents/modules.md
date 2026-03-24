# Modules

Schemata provides a modular architecture that discovers, loads, and runs application modules through assembly scanning and a structured lifecycle.

## Packages

| Package                 | Role                                       |
| ----------------------- | ------------------------------------------ |
| `Schemata.Abstractions` | `IModule`, `ModuleBase`, `ModuleAttribute` |
| `Schemata.Modular`      | Discovery, runner, feature                 |

## Core types (Schemata.Abstractions)

### IModule

Marker interface for modules. Extends `IFeature`, inheriting `Order` and `Priority` properties:

```csharp
public interface IModule : IFeature;
```

### ModuleBase

Abstract base class that provides default ordering:

```csharp
public abstract class ModuleBase : IModule
{
    public virtual int Order => 0;
    public virtual int Priority => Order;
}
```

- `Order` -- controls service registration sequence during `ConfigureServices`
- `Priority` -- controls middleware/endpoint registration sequence during `ConfigureApplication` and `ConfigureEndpoints`

### ModuleAttribute

Assembly-level attribute that declares a module for discovery:

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ModuleAttribute : Attribute
{
    public ModuleAttribute(string? name);
    public string Name { get; }
}
```

Apply to the entry assembly to register modules:

```csharp
[assembly: Module("MyCompany.MyModule")]
```

The `Name` is the assembly name that contains the module's `IModule` implementation.

## Discovery (Schemata.Modular)

### IModulesProvider

Discovers and provides the set of available modules:

```csharp
public interface IModulesProvider
{
    IEnumerable<ModuleDescriptor> GetModules();
}
```

### DefaultModulesProvider

The built-in provider. Scans the entry assembly for `ModuleAttribute` annotations, then for each:

1. Loads the referenced assembly via `Assembly.Load(module.Name)`
2. Finds the first concrete (non-abstract) type that implements `IModule`
3. Reads assembly metadata: `AssemblyProductAttribute`, `AssemblyDescriptionAttribute`, `AssemblyCompanyAttribute`, `AssemblyCopyrightAttribute`, `AssemblyInformationalVersionAttribute`
4. Constructs a `ModuleDescriptor`

The discovered modules are cached in a static `ConcurrentBag` to avoid repeated scanning.

### ModuleDescriptor

Contains all metadata about a discovered module:

- `Name` -- the assembly name
- `DisplayName` -- from `AssemblyProductAttribute`, defaults to `Name`
- `Description` -- from `AssemblyDescriptionAttribute`
- `Company` -- from `AssemblyCompanyAttribute`
- `Copyright` -- from `AssemblyCopyrightAttribute`, auto-generated if absent
- `Version` -- from `AssemblyInformationalVersionAttribute`, with git hash truncation for display
- `Assembly` -- the loaded `Assembly`
- `EntryType` -- the concrete `IModule` implementation type
- `ProviderType` -- the `IModulesProvider` type that discovered this module

## Lifecycle runner

### IModulesRunner

Orchestrates the three lifecycle phases:

```csharp
public interface IModulesRunner
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment);
    void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment);
    void ConfigureEndpoints(IApplicationBuilder app, IEndpointRouteBuilder endpoints, IConfiguration configuration, IWebHostEnvironment environment);
}
```

### DefaultModulesRunner

The built-in runner. Behavior during each phase:

**ConfigureServices:**

1. Retrieves module descriptors from `SchemataOptions`
2. Instantiates each module's `IModule` implementation
3. Sorts modules by `Order` (ascending)
4. For each module, registers it as a singleton (both by concrete type and as `IModule`)
5. Calls `ConfigureServices` via reflection if the method exists

**ConfigureApplication:**

1. Resolves all `IModule` instances from the service provider
2. Sorts by `Priority` (ascending)
3. Calls `ConfigureApplication` via reflection on each module that implements the method

**ConfigureEndpoints:**

1. Same resolution and sorting as `ConfigureApplication`
2. Calls `ConfigureEndpoints` via reflection on each module that implements the method

Methods are invoked via `Utilities.CallMethod`, which performs parameter injection from the DI container.

## UseModular()

```csharp
builder.UseModular();
```

### Overloads

- `UseModular()` -- uses `DefaultModulesRunner` and `DefaultModulesProvider`
- `UseModular<TRunner>()` -- uses a custom runner with the default provider
- `UseModular<TRunner, TProvider>()` -- uses custom runner and provider types

### Feature behavior

`SchemataModulesFeature` instantiates the provider, discovers modules, stores them in `SchemataOptions`, then creates and runs the module runner during each lifecycle phase:

1. **ConfigureServices** -- instantiates the provider, calls `GetModules()`, stores descriptors via `SchemataOptions.SetModules()`, creates the runner, calls `runner.ConfigureServices()`, and registers the runner as a singleton `IModulesRunner`
2. **ConfigureApplication** -- resolves the runner from DI and calls `runner.ConfigureApplication()`
3. **ConfigureEndpoints** -- resolves the runner from DI and calls `runner.ConfigureEndpoints()`

## SchemataOptions extensions

Two extension methods on `SchemataOptions` store and retrieve module descriptors:

- `GetModules()` -- returns `List<ModuleDescriptor>?`
- `SetModules(value)` -- stores the descriptors under the `SchemataConstants.Options.ModularModules` key

## Writing a module

1. Create a class library project
2. Implement `ModuleBase` (or `IModule`):

```csharp
public class MyModule : ModuleBase
{
    public override int Order => 100;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        // Register services
    }

    public void ConfigureApplication(IApplicationBuilder app)
    {
        // Configure middleware
    }

    public void ConfigureEndpoints(IEndpointRouteBuilder endpoints, IApplicationBuilder app)
    {
        // Map endpoints
    }
}
```

3. In the host application's `AssemblyInfo.cs` (or any file with assembly-level attributes):

```csharp
[assembly: Module("MyCompany.MyModule")]
```

The framework discovers the module at startup and invokes its lifecycle methods in the correct order.
