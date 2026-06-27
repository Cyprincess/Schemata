# Schemata.Core — Composition Root

`SchemataBuilder` + the 16 built-in ASP.NET features that wire the framework into a host. Every Schemata application transitively pulls this package.

## Layout

```
Schemata.Core/
├── SchemataBuilder.cs         # fluent builder; stages services, configurators, options
├── SchemataOptions.cs         # cross-feature key-value bag + feature registry
├── SchemataStartup.cs         # ASP.NET startup filter that runs the pipeline
├── SchemataExtensionPart.cs   # MVC application part hook for extension assemblies
├── Configurators.cs           # deferred Action<TOptions> / Action<T1,T2> registry
├── Services.cs                # internal IServiceCollection impl used for staging
├── Utilities.cs               # internal reflection helpers
├── WellKnownOptions.cs        # /.well-known/* route table
├── Extensions/                # public API (UseSchemata / Configure / Add / Map)
├── Features/                  # 16 ISimpleFeature implementations + FeatureBase
└── Json/                      # snake_case + 53-bit-safe number converters
```

## Public API Entry Points

- [Extensions/WebApplicationBuilderExtensions.cs](Extensions/WebApplicationBuilderExtensions.cs) — `UseSchemata(this WebApplicationBuilder, Action<SchemataBuilder>)`.
- [Extensions/ServiceCollectionExtensions.cs](Extensions/ServiceCollectionExtensions.cs) — `AddSchemata(...)` — registers the startup filter + flushes staged services.
- [Extensions/ApplicationBuilderExtensions.cs](Extensions/ApplicationBuilderExtensions.cs) — runs feature `ConfigureApplication` in `Priority` order.
- [Extensions/EndpointBuilderExtensions.cs](Extensions/EndpointBuilderExtensions.cs) — runs feature `ConfigureEndpoints` in `Priority` order; only invoked when an `EndpointDataSource` is registered.
- [SchemataBuilder.cs](SchemataBuilder.cs) — fluent surface: `AddFeature<T>`, `HasFeature<T>`, `Configure<TOptions>`, `Configure<T1,T2>`, `ConfigureServices(Action<IServiceCollection>)`, `Invoke(IServiceCollection)`, `CreateLogger<T>()`, `ReplaceLoggerFactory`.

## Built-in Features

All in [Features/](Features/), each a `FeatureBase` subclass. See [../../README.md](../../README.md) for the full priority table; this is the implementation map.

| File | Priority |
|---|---|
| `SchemataForwardedHeadersFeature.cs` | 100_000_000 |
| `SchemataDeveloperExceptionPageFeature.cs` | 110_000_000 |
| `SchemataLoggingFeature.cs` | 120_000_000 |
| `SchemataHttpLoggingFeature.cs` | 130_000_000 |
| `SchemataW3CLoggingFeature.cs` | 140_000_000 |
| `SchemataHttpsFeature.cs` | 150_000_000 |
| `SchemataCookiePolicyFeature.cs` | 170_000_000 |
| `SchemataRoutingFeature.cs` | 180_000_000 |
| `SchemataWellKnownFeature.cs` | 185_000_000 (sub-feature of Routing, +5M) |
| `SchemataQuotaFeature.cs` | 190_000_000 |
| `SchemataCorsFeature.cs` | 200_000_000 |
| `SchemataAuthenticationFeature.cs` | 210_000_000 |
| `SchemataSessionFeature.cs` | 220_000_000 |
| `SchemataControllersFeature.cs` | 230_000_000 |
| `SchemataJsonSerializerFeature.cs` | 240_000_000 |

Tenancy lives at `160_000_000` but the feature class is in `Schemata.Tenancy.Foundation`, not here.

## Conventions

- **Add a built-in feature** = new `Features/Schemata{X}Feature.cs` extending `FeatureBase` + matching `Use{X}(this SchemataBuilder)` in `Extensions/SchemataBuilderExtensions.cs`.
- **Pick an unused priority** in the table above. Stay outside `[100_000_000, 900_000_000]` if you are adding a non-built-in feature in your own app.
- **`Order` defaults to `Priority`** in `FeatureBase`. Override only when DI-registration order must differ from middleware order.
- **`SchemataStartup` is the only `IStartupFilter`** that should touch the Schemata pipeline. Do not register competing startup filters that re-run feature hooks.

## Anti-Patterns

- **Do NOT** mutate `SchemataBuilder.Services` from inside a feature's `ConfigureServices` — receive the host `IServiceCollection` parameter instead. The staging collection is for the builder phase only and gets cleared by `Invoke`.
- **Do NOT** rely on the order of `SchemataBuilder.AddFeature` calls; sequencing is by `Order`/`Priority`, not insertion.
- **Do NOT** read `IConfiguration` to decide whether to register services in `ConfigureApplication` — by then DI is already built. Decide in `ConfigureServices`.
- **Do NOT** swap `SchemataOptions.Logging` after features have begun configuring — call `ReplaceLoggerFactory` once, up front.

## Notes

- `SchemataJsonSerializerFeature` sets JSON to snake_case and adds `JsonStringNumberConverter` for safe 53-bit JS integer round-trip ([Json/JsonStringNumberConverter.cs](Json/JsonStringNumberConverter.cs)). Polymorphic resolution is handled by [Json/PolymorphicTypeResolver.cs](Json/PolymorphicTypeResolver.cs) driven by `[Polymorphic]` attributes from `Schemata.Abstractions`.
- `SchemataWellKnownFeature` reads [WellKnownOptions.cs](WellKnownOptions.cs); extensions (e.g. Authorization OIDC discovery) push entries into this table during `ConfigureServices`.
- `SchemataExtensionPart` is the hook by which extension packages register MVC application parts so their controllers are discovered.
