# Feature System

A **feature** is a unit of capability that hooks into three startup phases — service
registration, middleware pipeline, endpoint mapping — and runs in a deterministic order set by
two integer properties. The feature system orders the flat list of `Use*` calls into a
deterministic ASP.NET Core pipeline regardless of call order.

## Where the code lives

| Package                 | Key files                                                                                                 |
| ----------------------- | --------------------------------------------------------------------------------------------------------- |
| `Schemata.Core`         | `Features/ISimpleFeature.cs`, `Features/FeatureBase.cs`                                                   |
| `Schemata.Core`         | `Features/DependsOnAttribute.cs`, `Features/DependsOnAttribute\`1.cs`, `Features/InformationAttribute.cs` |
| `Schemata.Core`         | `SchemataBuilder.cs`, `Configurators.cs`, `Extensions/SchemataOptionsExtensions.cs`                       |
| `Schemata.Abstractions` | `IFeature.cs`, `SchemataConstants.cs` (`Orders`)                                                          |

## ISimpleFeature and FeatureBase

`Schemata.Core.Features.ISimpleFeature` extends `Schemata.Abstractions.IFeature` and declares the
three lifecycle hooks:

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

`IFeature` declares the two ordering properties:

```csharp
public interface IFeature
{
    int Order    { get; }
    int Priority { get; }
}
```

`FeatureBase` provides no-op implementations of all three hooks and the default ordering:

```csharp
public abstract class FeatureBase : ISimpleFeature
{
    public virtual int Order    => Priority;
    public virtual int Priority => int.MaxValue;
    // virtual no-op ConfigureServices / ConfigureApplication / ConfigureEndpoints
}
```

`Order` defaults to `Priority`. A feature whose service registration must sit at a different
position than its middleware overrides `Order` separately.

## Order vs Priority

| Property   | Controls                                                 | Sorted in                   |
| ---------- | -------------------------------------------------------- | --------------------------- |
| `Order`    | `ConfigureServices` sequence                             | `SchemataBuilder.Invoke`    |
| `Priority` | `ConfigureApplication` and `ConfigureEndpoints` sequence | `SchemataStartup.Configure` |

Both sorts are ascending; lower runs first. Ties resolve in dictionary iteration order.

`SchemataConstants.Orders` defines three anchors:

| Constant    | Value       | Purpose                                             |
| ----------- | ----------- | --------------------------------------------------- |
| `Base`      | 100,000,000 | Anchor for built-in core features and advisors      |
| `Extension` | 400,000,000 | Anchor for extension feature chains (`Base + 300M`) |
| `Max`       | 900,000,000 | Terminal anchor for units that must run last        |

The range `[100_000_000, 900_000_000]` is reserved for built-in and extension features. User
features pick values outside it. The complete table is in [Built-in Features](built-in-features.md).

## Lifecycle

### ConfigureServices

Runs inside `SchemataBuilder.Invoke` at `UseSchemata` time, after staged services flush into the
host container. Features are sorted by `Order`. Each receives the host `IServiceCollection`, the
shared `SchemataOptions`, the `Configurators` registry, the `IConfiguration`, and the
`IWebHostEnvironment`. A feature retrieves and consumes its user-supplied options delegate via
`configurators.PopOrDefault<TOptions>()`.

After all features run, `Configurators.Invoke(services)` wraps every remaining delegate as an
`IConfigureOptions<T>` singleton.

### ConfigureApplication

Runs from `SchemataStartup` (an `IStartupFilter`) ahead of the rest of the middleware pipeline.
Features are sorted by `Priority`. This is where a feature inserts middleware via `app.Use*(...)`.

### ConfigureEndpoints

Runs from `SchemataStartup` inside `app.UseEndpoints(...)`, and only when an `EndpointDataSource`
is registered. Features are sorted by `Priority`. This is where a feature maps routes on the
`IEndpointRouteBuilder`. Afterwards, `app.CleanSchemata()` drops the features dictionary.

## Use\* extension methods

A `Use*` method follows one pattern:

1. Accept an optional `Action<TOptions>` for the feature's options.
2. Stage that delegate with `builder.Configure<TOptions>(...)`.
3. Register the feature type with `builder.AddFeature<TFeature>()`.
4. Return the builder for chaining.

`UseRouting()` registers `SchemataRoutingFeature` directly. `UseCors(...)` stages a `CorsOptions`
delegate, then registers `SchemataCorsFeature`, which pops the delegate during `ConfigureServices`.

For registrations that belong to no feature, `SchemataBuilder.ConfigureServices(Action<IServiceCollection>)`
writes straight into the staging collection, flushed before any feature runs:

```csharp
builder.UseSchemata(schema => {
    schema.UseRouting();
    schema.ConfigureServices(services => {
        services.AddSingleton<IMyService, MyService>();
    });
});
```

## Configurators

`Configurators` is a `Type`-keyed registry of deferred `Action<T>` delegates. `Set<T>(action)`
chains: an existing delegate runs first, the new one after. Features consume entries via `Pop<T>()`
(throws `KeyNotFoundException` when absent) or `PopOrDefault<T>()` (returns a no-op when absent). A
two-parameter variant keyed by the `(T1, T2)` value-tuple handles callbacks such as
`AuthenticationBuilder` that take two arguments.

`Configurators.Invoke(services)` converts each surviving delegate into a `ConfigureNamedOptions<T>`
under `Options.DefaultName`, registered as an `IConfigureOptions<T>` singleton.

## DependsOn and Information

A feature declares prerequisites with two attribute forms, processed during
`SchemataOptionsExtensions.AddFeature`:

- **`[DependsOn<T>]`** (`DependsOnAttribute<T>`) — typed dependency. When the dependency is not
  already registered, `AddFeature` registers it first, recursively. `UseControllers()` pulls in
  `SchemataRoutingFeature` this way.
- **`[DependsOn("Fully.Qualified.Name")]`** (`DependsOnAttribute`) — string dependency for
  cross-assembly cases where the type cannot be referenced. The name resolves through
  `AppDomainTypeCache.GetType`, and `AddFeature` only checks `HasFeature(dependency)`; it does
  **not** auto-register. A missing required dependency logs at `LogLevel.Error`; setting
  `Optional = true` drops that to `LogLevel.Information`.
- **`[Information("message", args...)]`** — a registration-time log line, emitted only when
  `SchemataLoggingFeature` is registered. `Level` defaults to `Information`.
  `SchemataHttpLoggingFeature` carries two such warnings about performance and PII logging.

Only attributes in the `Schemata.Core.Features` namespace are processed; others are ignored.

Key dependency chains across built-in and extension features:

| Feature                        | Depends on                                                                                             |
| ------------------------------ | ------------------------------------------------------------------------------------------------------ |
| `SchemataControllersFeature`   | `SchemataRoutingFeature`                                                                               |
| `SchemataWellKnownFeature`     | `SchemataRoutingFeature`                                                                               |
| `SchemataSessionFeature<T>`    | `SchemataCookiePolicyFeature`                                                                          |
| `SchemataTransportHttpFeature` | `SchemataControllersFeature`, `SchemataJsonSerializerFeature`, `SchemataDeveloperExceptionPageFeature` |
| `SchemataTransportGrpcFeature` | `SchemataRoutingFeature`                                                                               |

## SchemataOptions

`SchemataOptions` is a `string`-keyed `object` bag plus an `ILoggerFactory`, registered as a
singleton:

```csharp
schemata.Set<MyConfig>("MyFeature:Config", config); // null removes the key
var keep = schemata.Get<MyConfig>("MyFeature:Config"); // read
var once = schemata.Pop<MyConfig>("MyFeature:Config"); // read and remove
```

The registered features live under the `"Features"` key. The extension methods
`AddFeature<T>()`, `HasFeature<T>()`, and `GetFeatures()` manage them. `AddFeature` deduplicates by
`RuntimeTypeHandle`, so `UseControllers()` called twice registers the feature once.
`HasFeature(typeof(SchemataSessionFeature<>))` is the open-generic check — it matches any closed
instantiation.

## Design rationale

Sorting `Order` and `Priority` independently lets a feature register its services at one point in
the DI graph and insert its middleware at another point in the pipeline. The tenancy feature uses
this: `Order` is `Orders.Max` (900M, services register last, after every store is set up) while
`Priority` is 160M (middleware runs early, before routing).

## Caveats

- `AddFeature` deduplicates by `RuntimeTypeHandle`. `SchemataSessionFeature<MyStore>` and
  `SchemataSessionFeature<OtherStore>` are distinct features; both register.
- `SchemataControllersFeature` removes every `Schemata.*` `AssemblyPart` from MVC's
  `ApplicationPartManager`. Expose a controller from a `Schemata.*` assembly by registering a
  `SchemataExtensionPart<T>` for it.
- A feature added during another feature's `ConfigureServices` misses `ConfigureServices` if it
  was not in the sorted list when `Invoke` ran.

## See also

- [Core Overview](overview.md) — startup sequence and the three-bucket model
- [Built-in Features](built-in-features.md) — the authoritative priority table
- [Advice Pipeline](advice-pipeline.md) — `IAdvisor`, `AdviceContext`, `AdviseResult`
