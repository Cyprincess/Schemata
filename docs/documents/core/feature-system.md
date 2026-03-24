# Feature System

The Feature System is the foundation of how Schemata organizes startup behavior. Every capability in the framework -- routing, authentication, CORS, JSON serialization, and so on -- is packaged as a **feature**: a class that hooks into three lifecycle phases (service registration, middleware pipeline, endpoint mapping) and is executed in a deterministic order.

## SchemataBuilder

`SchemataBuilder` is the fluent entry point for configuring Schemata. It is created internally when you call `UseSchemata` on a `WebApplicationBuilder`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.UseSchemata(schema => {
    schema.UseLogging();
    schema.UseRouting();
    schema.UseCors();
    schema.UseAuthentication(auth => auth.AddCookie());
    schema.UseControllers();
    schema.UseJsonSerializer();
});
```

### How UseSchemata works

`UseSchemata` is an extension method on `WebApplicationBuilder`. It delegates to `AddSchemata` on `IServiceCollection`, which:

1. Creates a new `SchemataBuilder` with the app's `IConfiguration` and `IWebHostEnvironment`.
2. Registers a `SchemataStartup` as an `IStartupFilter` so that middleware and endpoints are wired automatically.
3. Registers the builder's `SchemataOptions` as a singleton in the DI container.
4. Invokes the user's configuration lambda (the `Action<SchemataBuilder>` parameter).
5. Calls `builder.Invoke(services)`, which flushes all staged services and features into the host service collection.

There is also an overload that accepts a second lambda for direct `SchemataOptions` configuration:

```csharp
builder.UseSchemata(
    schema => { /* configure features */ },
    options => { /* configure SchemataOptions directly */ }
);
```

### UseXxx extension methods

Each `UseXxx` method on `SchemataBuilder` follows the same pattern:

1. Accept an optional `Action<TOptions>` for the feature's options.
2. Store the options action in the `Configurators` registry via `builder.Configure<TOptions>(...)`.
3. Register the feature type via `builder.AddFeature<TFeature>()`.
4. Return the builder for chaining.

For example, `UseRouting` registers `SchemataRoutingFeature`. `UseCors` stores a `CorsOptions` configurator and registers `SchemataCorsFeature`. The feature later retrieves its options from the `Configurators` during `ConfigureServices`.

### ConfigureServices on SchemataBuilder

`SchemataBuilder` exposes a `ConfigureServices` method for ad-hoc service registration that does not belong to any feature:

```csharp
builder.UseSchemata(schema => {
    schema.UseRouting();

    schema.ConfigureServices(services => {
        services.AddSingleton<IMyService, MyService>();
    });
});
```

The lambda receives a staging `IServiceCollection`. These services are flushed into the host container **before** any feature's `ConfigureServices` runs. This is useful for registering services that features may depend on during their own configuration.

## ISimpleFeature and FeatureBase

### ISimpleFeature

`ISimpleFeature` extends `IFeature` (from `Schemata.Abstractions`) and defines the three lifecycle hooks that the framework calls:

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

### IFeature

The base `IFeature` interface (in `Schemata.Abstractions`) declares the two ordering properties:

```csharp
public interface IFeature
{
    int Order { get; }
    int Priority { get; }
}
```

### FeatureBase

`FeatureBase` is the abstract base class that provides default no-op implementations of all three lifecycle methods. Most features extend this class and override only the hooks they need:

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

By default, `Order` returns the same value as `Priority`, and `Priority` defaults to `int.MaxValue` (placing unordered features at the end). Features override `Priority` to control their position in all phases. Features that need different ordering during service registration versus middleware/endpoint configuration override `Order` independently.

## Order and Priority

`Order` and `Priority` control execution sequence across two different phases:

| Property   | Used during                                                                | Sorted in                                                                              |
| ---------- | -------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| `Order`    | `ConfigureServices` (service registration)                                 | `SchemataBuilder.Invoke`                                                               |
| `Priority` | `ConfigureApplication` and `ConfigureEndpoints` (middleware and endpoints) | `ApplicationBuilderExtensions.UseSchemata` and `EndpointBuilderExtensions.UseSchemata` |

Lower values execute first. Both sorts use `CompareTo` in ascending order.

Most built-in features set only `Priority` and let `Order` inherit the same value (via the `FeatureBase` default). The tenancy feature is a notable exception -- it sets `Order` to `SchemataConstants.Orders.Max` (900,000,000) so its services are registered last, while its `Priority` places its middleware earlier in the pipeline.

### Reserved ranges

The framework defines three anchor constants in `SchemataConstants.Orders`:

| Constant    | Value       | Purpose                                                            |
| ----------- | ----------- | ------------------------------------------------------------------ |
| `Base`      | 100,000,000 | Anchor for built-in core features                                  |
| `Extension` | 400,000,000 | Anchor for extension features (identity, security, resource, etc.) |
| `Max`       | 900,000,000 | Terminal anchor for features that must run near the end            |

Built-in core features form a chain starting at `Base`, each adding 10,000,000 or 20,000,000 to the previous feature's `DefaultPriority`. The chain runs in this order:

**ForwardedHeaders** (100M) -> **DeveloperExceptionPage** (+10M) -> **ExceptionHandler** (+10M) -> **Logging** (+10M) -> **HttpLogging** (+10M) -> **W3CLogging** (+10M) -> **Https** (+10M) -> **CookiePolicy** (+20M) -> **Routing** (+10M) -> **Quota** (+10M) -> **Cors** (+10M) -> **Authentication** (+10M) -> **Session** (+10M) -> **Controllers** (+10M) -> **JsonSerializer** (+10M)

Extension features start at `Extension` (400M) with their own offsets:

- Identity: Extension + 10M
- Authorization: Extension + 20M
- Mapping: Extension + 30M
- Resource: Extension + 50M
- Modules: Extension + 80M

Custom features should use values above the last built-in chain value but below `Extension` for core-level features, or above the last extension value for extension-level features.

## SchemataOptions

`SchemataOptions` is a named key-value store shared across all features. It holds arbitrary objects keyed by string name, plus a logger factory for creating loggers.

### Storage methods

```csharp
// Store a value (passing null removes the key)
schemata.Set<MyConfig>("MyFeature:Config", config);

// Retrieve without removing
var config = schemata.Get<MyConfig>("MyFeature:Config");

// Retrieve and remove (one-time consumption)
var config = schemata.Pop<MyConfig>("MyFeature:Config");
```

All three methods use a `where TOptions : class` constraint.

### Feature storage

The registered features dictionary is stored inside `SchemataOptions` itself under the key `"Features"` (defined as `SchemataConstants.Options.Features`). Extension methods on `SchemataOptions` manage this:

- `GetFeatures()` -- retrieves the `Dictionary<RuntimeTypeHandle, ISimpleFeature>`.
- `SetFeatures(...)` -- stores the dictionary.
- `AddFeature<T>()` / `AddFeature(Type)` -- creates a feature instance and adds it to the dictionary. If the feature is already registered, it is skipped.
- `HasFeature<T>()` / `HasFeature(Type)` -- checks for registration, including open generic type definitions.

### Logging

`SchemataOptions` creates and owns an `ILoggerFactory`. Features and builder methods use `CreateLogger<T>()` to obtain loggers. The factory can be replaced via `ReplaceLoggerFactory`, which the `UseLogging` extension does.

## Feature lifecycle

Features participate in three phases, executed in this order during application startup:

### 1. ConfigureServices

Called during `SchemataBuilder.Invoke`, which runs inside `AddSchemata` (i.e., at `UseSchemata` time). Features are **sorted by `Order`** (ascending). Each feature receives:

- `IServiceCollection services` -- the host service collection.
- `SchemataOptions schemata` -- the shared options container.
- `Configurators configurators` -- the deferred configurator registry. Features call `configurators.PopOrDefault<TOptions>()` to retrieve and consume user-provided option actions.
- `IConfiguration configuration` -- the app configuration.
- `IWebHostEnvironment environment` -- the hosting environment.

After all features run, `Configurators.Invoke(services)` flushes any remaining configurators as `IConfigureOptions<T>` registrations into the DI container.

### 2. ConfigureApplication

Called by the `SchemataStartup` startup filter before the rest of the middleware pipeline. Features are **sorted by `Priority`** (ascending). This is where features add middleware via `app.UseXxx(...)`.

### 3. ConfigureEndpoints

Called by `SchemataStartup` inside `app.UseEndpoints(...)`, but only if an `EndpointDataSource` is registered. Features are **sorted by `Priority`** (ascending). This is where features map routes via the `IEndpointRouteBuilder`.

After both `ConfigureApplication` and `ConfigureEndpoints` complete, `CleanSchemata` removes the features dictionary from `SchemataOptions` (via `Pop`) to free memory.

## Feature dependencies

Features can declare dependencies using two attributes:

### DependsOn&lt;T&gt;

The generic attribute takes a feature type. When the dependent feature is registered, the framework checks whether the dependency is already present. If not, it is **automatically registered**:

```csharp
[DependsOn<SchemataRoutingFeature>]
[DependsOn<SchemataExceptionHandlerFeature>]
public sealed class SchemataControllersFeature : FeatureBase { ... }
```

### DependsOn (string name)

The non-generic attribute takes a fully-qualified type name. This is used for cross-assembly dependencies where the type may not be directly referenceable. It logs a warning or error but does **not** auto-register:

```csharp
[DependsOn("Schemata.Mapping.Foundation.Features.SchemataMappingFeature`1")]
[DependsOn("Schemata.Security.Foundation.Features.SchemataSecurityFeature")]
public sealed class SchemataResourceFeature : FeatureBase { ... }
```

The `Optional` property controls severity: when `true`, a missing dependency logs at `Information` level; when `false` (the default), it logs at `Error` level.

### Information

The `[Information]` attribute attaches a log message that is emitted when the feature is registered (only if the logging feature is active):

```csharp
[Information("My feature initialized with mode {Mode}", "default", Level = LogLevel.Debug)]
```

## How to create a custom feature

### 1. Define the feature class

Extend `FeatureBase` and override `Priority` (and optionally `Order`). Override only the lifecycle methods you need:

```csharp
public sealed class MyCustomFeature : FeatureBase
{
    public override int Priority => SchemataControllersFeature.DefaultPriority + 5_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.PopOrDefault<MyOptions>();

        services.AddSingleton<IMyService, MyService>();
        services.Configure<MyOptions>(configure);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseMiddleware<MyMiddleware>();
    }
}
```

### 2. Create an extension method

Follow the `UseXxx` pattern to expose the feature on `SchemataBuilder`:

```csharp
public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseMyFeature(
        this SchemataBuilder builder,
        Action<MyOptions>?   configure = null
    ) {
        configure ??= _ => { };
        builder.Configure(configure);
        builder.AddFeature<MyCustomFeature>();
        return builder;
    }
}
```

### 3. Register it

```csharp
builder.UseSchemata(schema => {
    schema.UseRouting();
    schema.UseMyFeature(options => {
        options.Setting = "value";
    });
});
```

### 4. Declare dependencies (optional)

If your feature requires other features, annotate the class:

```csharp
[DependsOn<SchemataRoutingFeature>]
public sealed class MyCustomFeature : FeatureBase { ... }
```

Missing generic dependencies are auto-registered. This means the user does not need to explicitly call `UseRouting` if your feature depends on it -- but the auto-registered feature will use default options.

## Configurators

The `Configurators` class is a type-keyed registry that accumulates configuration actions during builder setup and provides them to features at registration time. Each `UseXxx` call stores an `Action<TOptions>` via `builder.Configure<TOptions>(...)`, and the corresponding feature retrieves it with `configurators.PopOrDefault<TOptions>()`.

If multiple calls configure the same type, the actions are **chained** -- each new action wraps the previous one so they execute in registration order.

After all features have run their `ConfigureServices`, `Configurators.Invoke(services)` converts any remaining (unconsumed) configurators into `IConfigureOptions<T>` registrations in the DI container.

See [advice-pipeline.md](advice-pipeline.md) for how `IFeature` ordering relates to the advice pipeline. See [built-in-features.md](built-in-features.md) for a reference of all features shipped with the framework.
