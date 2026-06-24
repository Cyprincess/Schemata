# Schemata.Core

Hub of the framework: `SchemataBuilder`, the feature lifecycle, and all built-in middleware features (slots 100M-240M).

## Layout

```
Features/                15 built-in features + ISimpleFeature/FeatureBase/DependsOn
Extensions/              SchemataBuilder, options, app, endpoint, web-host wiring
Json/                    JsonStringNumberConverter + PolymorphicTypeResolver
SchemataBuilder.cs       Fluent builder; AddFeature<T>(), Invoke(), HasFeature<T>()
SchemataStartup.cs       IStartupFilter that runs app/endpoint phases by Priority
SchemataOptions.cs       Keyed bag shared across features
Configurators.cs         Deferred service-collection actions
Services.cs              Internal staging IServiceCollection
```

## Built-in Slots (`Priority == Order`)

| Slot | Feature |
|---|---|
| 100M | ForwardedHeaders |
| 110M | DeveloperExceptionPage |
| 120M | Logging |
| 130M | HttpLogging |
| 140M | W3CLogging |
| 150M | Https |
| 170M | CookiePolicy |
| 180M | Routing |
| 185M | WellKnown (`+5M` sub-slot of Routing) |
| 190M | Quota |
| 200M | Cors |
| 210M | Authentication |
| 220M | Session (`SchemataSessionFeature<T>`) |
| 230M | Controllers |
| 240M | JsonSerializer |

Slot 160M is reserved for `Schemata.Tenancy.Foundation.SchemataTenancyFeature<TM,TT>` (declared outside Core; `Order` overridden to 900M).

## Lifecycle Internals

- `SchemataBuilder.Invoke(IServiceCollection)` sorts features by `Order`, runs `ConfigureServices`, then flushes `Configurators` against the host's services.
- `SchemataStartup` runs `app.UseSchemata(...)` (app phase), then `app.UseEndpoints(...)` containing endpoint phase, then `app.CleanSchemata()`.
- `SchemataOptionsExtensions.AddFeature(...)` instantiates with an `ILogger`, stores by `RuntimeTypeHandle`, walks `[DependsOn<T>]` recursively. String-form `[DependsOn("Type, Asm")]` is logged only - it does not auto-register.

## Where To Look

| Task | File |
|---|---|
| Add a `Use*()` activator on `SchemataBuilder` | [Extensions/SchemataBuilderExtensions.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Core/Extensions/SchemataBuilderExtensions.cs) |
| Change service flush order | [SchemataBuilder.cs#Invoke](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Core/SchemataBuilder.cs) |
| Change app/endpoint phase wiring | [SchemataStartup.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Core/SchemataStartup.cs) + [Extensions/ApplicationBuilderExtensions.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Core/Extensions/ApplicationBuilderExtensions.cs) + [Extensions/EndpointBuilderExtensions.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Core/Extensions/EndpointBuilderExtensions.cs) |
| Add a built-in middleware feature | [Features/](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Core/Features/) - copy a sibling, pick an unused 10M-wide slot |

## Rules

- JSON serializer feature tweaks `JsonSerializerOptions` unconditionally; MVC `JsonOptions` are wired only when `SchemataControllersFeature` is registered.
- On `NET10_0_OR_GREATER` the forwarded-header default clears `KnownIPNetworks`; older TFMs clear `KnownNetworks` - keep both branches.
- `[Information]` log lines emit only when `SchemataLoggingFeature` is present; ordering matters for visibility during startup.
- `WellKnown` lives at `Routing+5M` deliberately so `/.well-known/*` registers before user-defined endpoints. Do not shift it.
