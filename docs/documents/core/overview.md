# Core Overview

`Schemata.Core` is the foundation package. It owns the startup sequence, the feature
registration model, the three-bucket builder, and the shared options store. Every Foundation-tier
package depends on it. Its own dependencies stay within the abstractions tier —
`Schemata.Abstractions` and `Schemata.Common`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Core` | `SchemataBuilder.cs`, `SchemataOptions.cs`, `Configurators.cs`, `Services.cs`, `SchemataStartup.cs` |
| `Schemata.Core` | `Extensions/WebApplicationBuilderExtensions.cs`, `Extensions/ServiceCollectionExtensions.cs`, `Extensions/SchemataOptionsExtensions.cs` |
| `Schemata.Core` | `Features/ISimpleFeature.cs`, `Features/FeatureBase.cs`, `Features/DependsOnAttribute.cs` |
| `Schemata.Abstractions` | `SchemataConstants.cs` (`Orders`), `IFeature.cs`, `Advisors/IAdvisor.cs` |

## Startup sequence

The entry point is `Microsoft.AspNetCore.Builder.WebApplicationBuilder.UseSchemata(schema, configure)`,
defined in `WebApplicationBuilderExtensions.cs`. It delegates to
`services.AddSchemata(configuration, environment, schema, configure)` in `ServiceCollectionExtensions.cs`.

`AddSchemata` runs in order:

1. Constructs a `Schemata.Core.SchemataBuilder` from the application `IConfiguration` and
   `IWebHostEnvironment`.
2. Registers `Schemata.Core.SchemataStartup` as an `IStartupFilter` via `TryAddEnumerable`
   (transient).
3. Registers `builder.Options` (`SchemataOptions`) and `TimeProvider.System` as singletons.
4. Invokes `schema(builder)` — the user callback that calls `UseLogging()`, `UseRouting()`,
   `UseControllers()`, and so on. Each `Use*` call writes to the builder's three buckets and
   registers a feature type.
5. Invokes `configure(builder.Options)` — the optional direct-mutation callback.
6. Calls `builder.Invoke(services)`, which flushes staged services into the host container and
   runs feature `ConfigureServices` in `Order` sequence.

At host startup, `SchemataStartup.Configure(next)` runs as an `IStartupFilter`:

1. `app.UseSchemata(configuration, environment)` — sorts features by `Priority` and calls
   `ConfigureApplication` on each.
2. When an `EndpointDataSource` is registered, `app.UseEndpoints(ep => ep.UseSchemata(...))` —
   sorts features by `Priority` and calls `ConfigureEndpoints` on each.
3. `app.CleanSchemata()` — removes the features dictionary from `SchemataOptions` so it can be
   collected.
4. `next(app)` continues the filter chain.

## The three buckets

`SchemataBuilder` exposes three staging areas:

- **`Services`** (`Services.cs`) — an in-memory `IServiceCollection`. `Use*` extensions and
  `builder.ConfigureServices(...)` write here. `Invoke` copies each entry into the host container,
  preserving insertion order, then clears the buffer.
- **`Configurators`** (`Configurators.cs`) — a `Type`-keyed registry of deferred `Action<T>`
  options delegates. `Set<T>(action)` chains a new delegate after any existing one for the same
  type. Features consume entries via `Pop<T>()` or `PopOrDefault<T>()`. Entries left after all
  features run are wrapped as `IConfigureOptions<T>` singletons.
- **`Options`** (`SchemataOptions.cs`) — a `string`-keyed `object` bag plus the build-time
  `ILoggerFactory`. Registered as a singleton; lives for the application lifetime. Its
  `"Features"` entry is removed by `CleanSchemata()` after middleware configuration completes.

A `Use*` extension touches only these three buckets; it adds nothing to the host container
directly.

## Package structure

| Package | Role |
| --- | --- |
| `Schemata.Core` | Builder, startup, built-in features |
| `Schemata.Abstractions` | Contracts: `IFeature`, `IAdvisor`, `AdviceContext`, `SchemataConstants`, error and exception types |
| `Schemata.Advice` | Runtime: the `AdviceRunner` family, `AdvicePipeline<TAdvisor>`, the `Advisor` entry point |
| `Schemata.Advice.Generator` | Roslyn source generator emitting `RunAsync` extension methods |

`Schemata.Core` references `Schemata.Abstractions`. `Schemata.Advice` references
`Schemata.Abstractions`. The generator references only `Microsoft.CodeAnalysis.CSharp` and is
consumed as an analyzer by every `src/` project.

## Extension points

- Add a feature: implement `ISimpleFeature` (or extend `FeatureBase`) and call
  `builder.AddFeature<T>()`.
- Add a `Use*` extension method on `SchemataBuilder` in the `Microsoft.AspNetCore.Builder`
  namespace, so it surfaces alongside the built-in methods.
- Replace the build-time logger factory via `builder.ReplaceLoggerFactory(factory)`.

## Design rationale

The three-bucket pattern decouples the fluent API from the DI container. Because `Use*` writes to
a staging collection, features can inspect and rewrite each other's pending registrations during
`ConfigureServices` before anything reaches the live container. The deferred `Configurators`
registry lets independent `Use*` calls compose options delegates for the same type, and lets a
feature claim a delegate via `Pop` so it applies exactly once.

## Caveats

- `Invoke` clears `builder.Services` after flushing. A service added to `builder.Services` after
  `Invoke` runs is dropped.
- `SchemataOptions` outlives the build, but its `"Features"` entry is removed by `CleanSchemata()`.
  Reading features from `SchemataOptions` after the pipeline is built yields nothing.
- A feature added during another feature's `ConfigureServices` reaches `ConfigureApplication` and
  `ConfigureEndpoints` only if it entered the dictionary before `Invoke` snapshotted the sorted
  list. It misses `ConfigureServices`.

## See also

- [Feature System](feature-system.md) — lifecycle, `DependsOn`, `Order` vs `Priority`
- [Built-in Features](built-in-features.md) — the authoritative priority table
- [Advice Pipeline](advice-pipeline.md) — `IAdvisor`, `AdviceContext`, `AdviseResult`
