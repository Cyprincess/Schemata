# Core Overview

`Schemata.Core` is the mandatory foundation package. It owns the startup sequence, the feature registration model, the three-bucket builder, and the shared options store. Every Foundation-tier package depends on it. Its own dependencies stay inside the Schemata.Abstractions tier — `Schemata.Abstractions` and `Schemata.Common` — so it never reaches into a subsystem package.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Core` | `SchemataBuilder.cs`, `SchemataOptions.cs`, `Configurators.cs`, `Services.cs`, `SchemataStartup.cs` |
| `Schemata.Core` | `Extensions/WebApplicationBuilderExtensions.cs`, `Extensions/ServiceCollectionExtensions.cs`, `Extensions/SchemataBuilderExtensions.cs` |
| `Schemata.Core` | `Features/ISimpleFeature.cs`, `Features/FeatureBase.cs`, `Features/DependsOnAttribute.cs`, `Features/DependsOnAttribute\`1.cs` |
| `Schemata.Abstractions` | `SchemataConstants.cs` (Orders class), `Advisors/IAdvisor.cs`, `Advisors/AdviceContext.cs`, `Advisors/AdviseResult.cs` |

## Startup sequence

The entry point is `WebApplicationBuilder.UseSchemata(schema, configure)`, defined in `WebApplicationBuilderExtensions.cs`. It delegates immediately to `services.AddSchemata(cfg, env, schema, configure)` in `ServiceCollectionExtensions.cs`.

`AddSchemata` does the following in order:

1. Creates a `SchemataBuilder` with the application `IConfiguration` and `IWebHostEnvironment`.
2. Registers `SchemataStartup` as an `IStartupFilter` via `TryAddEnumerable` (transient — the host resolves it once during `Configure`).
3. Registers `SchemataOptions` as a singleton.
4. Calls `schema(builder)` — the user callback that calls `UseLogging()`, `UseRouting()`, `UseControllers()`, and so on. Each `Use*` call writes to the builder's three buckets and registers a feature type.
5. Calls `configure(builder.Options)` — the optional direct-mutation callback.
6. Calls `builder.Invoke(services)`, which flushes staged services and runs feature `ConfigureServices` in `Order` sequence.

At host startup, `SchemataStartup.Configure(next)` runs as an `IStartupFilter`:

1. Calls `app.UseSchemata(cfg, env)` — iterates features sorted by `Priority` and calls `ConfigureApplication` on each.
2. If an `EndpointDataSource` is registered, calls `app.UseEndpoints(ep => ep.UseSchemata(app, cfg, env))` — iterates features sorted by `Priority` and calls `ConfigureEndpoints` on each.
3. Calls `app.CleanSchemata()` — removes the features dictionary from `SchemataOptions` so it can be garbage-collected.
4. Calls `next(app)` to continue the `IStartupFilter` chain.

## The three buckets

`SchemataBuilder` owns three independent staging areas:

- **`Services`** (`Services.cs`) — an internal `IServiceCollection` buffer. `Use*` extensions write here. `Invoke` flushes them into the host container before features run, preserving insertion order.
- **`Configurators`** (`Configurators.cs`) — a `Type`-keyed dictionary of deferred `Action<T>` delegates. `Set<T>(action)` merges (post-write composes after pre-write). Features consume entries via `Pop<T>()` or `PopOrDefault<T>()`. Anything remaining after all features run is reified as `IConfigureOptions<T>` singletons via `ConfigureNamedOptions<T>`.
- **`Options`** (`SchemataOptions.cs`) — a `string`-keyed `object` bag plus the in-build `ILoggerFactory`. Registered as a singleton; lives for the application lifetime. The `"Features"` dictionary inside it is removed by `CleanSchemata()` after middleware configuration completes.

The rule: a `Use*` extension touches only these three buckets. It never calls `services.Add*` directly on the host container.

## Package structure

```
Schemata.Core                  — builder, startup, built-in features
Schemata.Abstractions          — contracts: IAdvisor, AdviceContext, SchemataConstants, error types
Schemata.Advice                — runtime: AdviceRunner family, AdvicePipeline, Advisor entry point
Schemata.Advice.Generator      — Roslyn source generator: emits RunAsync extensions
```

`Schemata.Core` references `Schemata.Abstractions`. `Schemata.Advice` references `Schemata.Abstractions`. The generator references only `Microsoft.CodeAnalysis.CSharp` and is consumed as an analyzer by every `src/` project.

## Extension points

- Add a feature by implementing `ISimpleFeature` (or inheriting `FeatureBase`) and calling `builder.AddFeature<T>()`.
- Add a `Use*` extension method on `SchemataBuilder` in the `Microsoft.AspNetCore.Builder` namespace (with `// ReSharper disable once CheckNamespace`) so it appears alongside the built-in methods.
- Replace the logger factory during build time via `builder.ReplaceLoggerFactory(factory)`.

## Design motivation

The three-bucket pattern exists to decouple the user-facing fluent API from the DI container. Features can inspect and modify each other's staged registrations during `ConfigureServices` without touching the live container. The deferred `Configurators` dictionary lets multiple `Use*` calls compose their options delegates without knowing about each other, and lets features take ownership of a delegate via `Pop` to prevent double-application.

## Caveats

- `Services.Clear()` is called inside `Invoke` after flushing. Any service added to `builder.Services` after `Invoke` runs will be silently dropped.
- `SchemataOptions` is a singleton but its `"Features"` key is removed by `CleanSchemata()`. Code that reads features from `SchemataOptions` after the middleware pipeline is built will find an empty result.
- Features added during another feature's `ConfigureServices` are picked up by `ConfigureApplication` and `ConfigureEndpoints` only if they were already in the sorted list when `Invoke` ran. Late additions miss `ConfigureServices`.

## See also

- [Feature System](feature-system.md) — detailed lifecycle, `DependsOn`, `Order` vs `Priority`
- [Advice Pipeline](advice-pipeline.md) — `IAdvisor`, `AdviceContext`, `AdviseResult`
- [Built-in Features](built-in-features.md) — authoritative priority table
- [JSON Serialization](json-serialization.md) — `SchemataJsonSerializerFeature` configuration
- [Error Model](error-model.md) — `SchemataException` and structured responses
