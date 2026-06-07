# Feature System

Every capability in Schemata is packaged as a **feature**: a class that hooks into three lifecycle phases (service registration, middleware pipeline, endpoint mapping) and executes in a deterministic order controlled by two integer properties. The feature system is the mechanism that turns a flat list of `Use*` calls into a correctly-ordered ASP.NET Core pipeline.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Core` | `Features/ISimpleFeature.cs`, `Features/FeatureBase.cs` |
| `Schemata.Core` | `Features/DependsOnAttribute.cs`, `Features/DependsOnAttribute\`1.cs`, `Features/InformationAttribute.cs` |
| `Schemata.Core` | `SchemataBuilder.cs`, `Configurators.cs`, `SchemataOptions.cs` |
| `Schemata.Core` | `Extensions/SchemataBuilderExtensions.cs` |
| `Schemata.Abstractions` | `SchemataConstants.cs` (Orders class) |

## ISimpleFeature and FeatureBase

`ISimpleFeature` (in `Schemata.Core.Features`) extends `IFeature` (from `Schemata.Abstractions`) and defines three lifecycle hooks:

```csharp
public interface ISimpleFeature : IFeature
{
    void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    );

    void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    );

    void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    );
}
```

`IFeature` (from `Schemata.Abstractions`) declares the two ordering properties:

```csharp
public interface IFeature
{
    int Order    { get; }
    int Priority { get; }
}
```

`FeatureBase` is the abstract base class providing no-op implementations of all three hooks. Most features extend it and override only what they need:

```csharp
public abstract class FeatureBase : ISimpleFeature
{
    public virtual int Order    => Priority;
    public virtual int Priority => int.MaxValue;

    public virtual void ConfigureServices(...)    { }
    public virtual void ConfigureApplication(...) { }
    public virtual void ConfigureEndpoints(...)   { }
}
```

`Order` defaults to `Priority`. Features that need different ordering for service registration versus middleware activation override `Order` independently.

## Order vs Priority

These two properties control execution sequence across two different phases:

| Property | Controls | Sorted in |
| --- | --- | --- |
| `Order` | `ConfigureServices` sequence | `SchemataBuilder.Invoke` |
| `Priority` | `ConfigureApplication` and `ConfigureEndpoints` sequence | `SchemataStartup.Configure` |

Both sorts are ascending. Lower values execute first. Most built-in features set only `Priority` and let `Order` inherit the same value via the `FeatureBase` default.

`SchemataConstants.Orders` defines three anchor constants:

| Constant | Value | Purpose |
| --- | --- | --- |
| `Base` | 100,000,000 | Anchor for built-in core features |
| `Extension` | 400,000,000 | Anchor for extension feature chains |
| `Max` | 900,000,000 | Terminal anchor for features that must run last |

The range `[100_000_000, 900_000_000]` is reserved for built-in and extension features. User features pick values outside that range. See [Built-in Features](built-in-features.md) for the complete priority table.

## Feature lifecycle

Features participate in three phases during application startup:

### 1. ConfigureServices

Called during `SchemataBuilder.Invoke`, which runs inside `AddSchemata` at `UseSchemata` time. Features are sorted by `Order` (ascending). Each feature receives:

- `IServiceCollection services` — the host service collection.
- `SchemataOptions schemata` — the shared options container.
- `Configurators configurators` — the deferred configurator registry. Features call `configurators.PopOrDefault<TOptions>()` to retrieve and consume user-provided option delegates.
- `IConfiguration configuration` — the application configuration.
- `IWebHostEnvironment environment` — the hosting environment.

After all features run, `Configurators.Invoke(services)` flushes any remaining (unconsumed) configurators as `IConfigureOptions<T>` singletons into the DI container.

### 2. ConfigureApplication

Called by `SchemataStartup` (an `IStartupFilter`) before the rest of the middleware pipeline. Features are sorted by `Priority` (ascending). This is where features add middleware via `app.Use*(...)`.

### 3. ConfigureEndpoints

Called by `SchemataStartup` inside `app.UseEndpoints(...)`, but only if an `EndpointDataSource` is registered. Features are sorted by `Priority` (ascending). This is where features map routes via `IEndpointRouteBuilder`.

After both phases complete, `app.CleanSchemata()` removes the features dictionary from `SchemataOptions` to free memory.

## Use* extension methods

Each `Use*` method on `SchemataBuilder` follows the same pattern:

1. Accept an optional `Action<TOptions>` for the feature's options.
2. Store the options delegate in `Configurators` via `builder.Configure<TOptions>(...)`.
3. Register the feature type via `builder.AddFeature<TFeature>()`.
4. Return the builder for chaining.

`UseRouting` registers `SchemataRoutingFeature` directly. `UseCors` stores a `CorsOptions` delegate first and then registers `SchemataCorsFeature`; the feature retrieves the delegate from `Configurators` during `ConfigureServices`.

Because features are sorted by `Priority` at startup, the call order of `Use*` methods is irrelevant. The pipeline is always deterministic.

For service registrations that do not belong to any feature, `SchemataBuilder.ConfigureServices(Action<IServiceCollection>)` exposes a staging collection that is flushed into the host container before any feature's `ConfigureServices` runs:

```csharp
builder.UseSchemata(schema => {
    schema.UseRouting();
    schema.ConfigureServices(services => {
        services.AddSingleton<IMyService, MyService>();
    });
});
```

## Configurators

`Configurators` is a `Type`-keyed dictionary of deferred `Action<T>` delegates. `Set<T>(action)` merges: if a delegate for `T` already exists, the new one is chained after it (post-write composes after pre-write). Features consume entries via `Pop<T>()` (throws if absent) or `PopOrDefault<T>()` (returns a no-op if absent).

A two-parameter variant keyed by `(T1, T2)` value-tuple handles callbacks like `AuthenticationBuilder` that require two arguments.

After all features have run `ConfigureServices`, `Configurators.Invoke(services)` converts any remaining unconsumed delegates into `IConfigureOptions<T>` registrations via `ConfigureNamedOptions<T>` and `Activator.CreateInstance`.

## DependsOn

Features declare dependencies using two attribute forms:

**`[DependsOn<T>]`** (generic, `DependsOnAttribute<T>`) — typed dependency. The dependency is automatically registered before the declaring feature during `SchemataOptionsExtensions.AddFeature`. The recursion is automatic, so `UseControllers()` pulls in `SchemataRoutingFeature` without the user naming it.

**`[DependsOn("Fully.Qualified.Name")]`** (string, `DependsOnAttribute`) — for cross-assembly dependencies where the type cannot be directly referenced. Resolved via `AppDomainTypeCache.GetType`. The string form does **not** auto-register the dependency; it only checks `HasFeature(dependency)` after resolving the name. Setting `Optional = true` downgrades a missing dependency from `LogLevel.Error` to `LogLevel.Information`.

**`[Information("message", args...)]`** — attaches a log line that the framework emits when the feature is registered, provided `SchemataLoggingFeature` is active. `Level` controls the severity (default `Information`). `SchemataHttpLoggingFeature` carries one warning about PII logging.

Key dependency chains in the built-in and extension features:

| Feature | Depends on |
| --- | --- |
| `SchemataTransportHttpFeature` | `SchemataDeveloperExceptionPageFeature`, `SchemataControllersFeature`, `SchemataJsonSerializerFeature` |
| `SchemataTransportGrpcFeature` | `SchemataRoutingFeature` |
| `SchemataSessionFeature<T>` | `SchemataCookiePolicyFeature` |
| `SchemataControllersFeature` | `SchemataRoutingFeature` |

## SchemataOptions

`SchemataOptions` is a `string`-keyed `object` bag plus an `ILoggerFactory`. It is registered as a singleton and shared across all features.

```csharp
// Store a value (null removes the key)
schemata.Set<MyConfig>("MyFeature:Config", config);

// Retrieve without removing
var config = schemata.Get<MyConfig>("MyFeature:Config");

// Retrieve and remove (one-time consumption)
var config = schemata.Pop<MyConfig>("MyFeature:Config");
```

The registered features dictionary is stored inside `SchemataOptions` under the key `"Features"`. Extension methods on `SchemataOptions` manage it: `AddFeature<T>()`, `HasFeature<T>()`, `GetFeatures()`. `AddFeature` deduplicates by `RuntimeTypeHandle`, so calling `UseControllers()` twice registers the feature only once.

`HasFeature(typeof(SchemataSessionFeature<>))` is the open-generic existence check — it matches any closed instantiation of the generic feature.

## Extension points

- Implement `ISimpleFeature` (or extend `FeatureBase`) and call `builder.AddFeature<T>()`.
- Add a `Use*` extension method on `SchemataBuilder` in the `Microsoft.AspNetCore.Builder` namespace (with `// ReSharper disable once CheckNamespace`) so it appears alongside the built-in methods.
- Declare `[DependsOn<T>]` on your feature class to pull in prerequisites automatically.

## Design motivation

Sorting by `Order` and `Priority` independently lets a feature register its services at one position in the DI graph while inserting its middleware at a different position in the pipeline. The tenancy feature uses this: its `Order` is `900_000_000` (services register last, after all other features have set up their stores) while its `Priority` is `160_000_000` (middleware runs early, before routing).

The `Configurators` dictionary decouples the user-facing fluent API from the DI container. Multiple `Use*` calls can compose their options delegates without knowing about each other, and features can take ownership of a delegate via `Pop` to prevent double-application.

## Caveats

- Features added during another feature's `ConfigureServices` are picked up by `ConfigureApplication` and `ConfigureEndpoints` only if they were already in the sorted list when `Invoke` ran. Late additions miss `ConfigureServices`.
- `SchemataControllersFeature` strips every `Schemata.*` `AssemblyPart` from MVC's `ApplicationPartManager`. To expose a controller from a `Schemata.*` assembly, register a `SchemataExtensionPart<T>` for it.
- `AddFeature` deduplicates by `RuntimeTypeHandle`. `SchemataSessionFeature<MyStore>` and `SchemataSessionFeature<OtherStore>` are two different features and both will be registered.

## See also

- [Core Overview](overview.md) — startup sequence and three-bucket model
- [Built-in Features](built-in-features.md) — authoritative priority table
- [Advice Pipeline](advice-pipeline.md) — `IAdvisor`, `AdviceContext`, `AdviseResult`
