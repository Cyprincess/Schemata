# Built-in Features

Schemata organizes its middleware pipeline and service registration through **features** — classes that implement `ISimpleFeature` (typically by extending `FeatureBase`). Each feature declares a `Priority` (and optionally a separate `Order`) that determines the order in which it configures middleware and registers services. Features are activated on `SchemataBuilder` via `Use*()` extension methods.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Core` | `Features/FeatureBase.cs`, `Features/ISimpleFeature.cs` |
| `Schemata.Core` | `Features/SchemataForwardedHeadersFeature.cs` .. `Features/SchemataJsonSerializerFeature.cs` |
| `Schemata.Core` | `Extensions/SchemataBuilderExtensions.cs` |
| `Schemata.Abstractions` | `SchemataConstants.cs` (Orders class) |

## Ordering model

Every feature exposes two ordering properties:

- **`Priority`** — controls the order in which `ConfigureApplication` and `ConfigureEndpoints` are called.
- **`Order`** — defaults to `Priority` unless overridden; controls the order in which `ConfigureServices` is called.

`FeatureBase.Order` returns `Priority` by default, so a feature can override only `Priority` and get both. Override `Order` separately when DI registration must run at a different position than middleware activation.

`SchemataConstants.Orders` defines three anchor constants:

| Constant | Value | Purpose |
| --- | --- | --- |
| `Base` | 100,000,000 | Anchor for built-in core features |
| `Extension` | 400,000,000 | Anchor for extension feature chains |
| `Max` | 900,000,000 | Terminal anchor for features that must run last |

The range `[100_000_000, 900_000_000]` is reserved for built-in and extension features. User features pick values outside that range.

## Built-in features (Schemata.Core)

These features ship in `Schemata.Core` and cover the fundamental ASP.NET Core middleware pipeline. The chain starts at `Orders.Base` (100M) with 10M strides. `WellKnown` is a sub-feature of `Routing` and uses a +5M offset.

| Priority | Feature class | `Use*()` method | What it registers |
| --- | --- | --- | --- |
| 100,000,000 | `SchemataForwardedHeadersFeature` | `UseForwardedHeaders()` | `ForwardedHeaders` middleware (`XForwardedFor`, `XForwardedProto`) |
| 110,000,000 | `SchemataDeveloperExceptionPageFeature` | `UseDeveloperExceptionPage()` | `DeveloperExceptionPage` middleware (Development only) |
| 120,000,000 | `SchemataLoggingFeature` | `UseLogging()` | `ILoggingBuilder` services via `AddLogging` |
| 130,000,000 | `SchemataHttpLoggingFeature` | `UseHttpLogging()` | `HttpLogging` services and middleware |
| 140,000,000 | `SchemataW3CLoggingFeature` | `UseW3CLogging()` | `W3CLogging` services and middleware |
| 150,000,000 | `SchemataHttpsFeature` | `UseHttps()` | `UseHsts` and `UseHttpsRedirection` middleware (non-Development only) |
| 160,000,000 | _(Tenancy — see extension table)_ | | |
| 170,000,000 | `SchemataCookiePolicyFeature` | `UseCookiePolicy()` | `CookiePolicy` services and middleware |
| 180,000,000 | `SchemataRoutingFeature` | `UseRouting()` | `Routing` services and middleware |
| 185,000,000 | `SchemataWellKnownFeature` | `UseWellKnown()` | `/.well-known/` routes (+5M sub-feature of Routing) |
| 190,000,000 | `SchemataQuotaFeature` | `UseQuota()` | `RateLimiter` services and middleware |
| 200,000,000 | `SchemataCorsFeature` | `UseCors()` | `CORS` services and middleware |
| 210,000,000 | `SchemataAuthenticationFeature` | `UseAuthentication()` | `Authentication` and `Authorization` services and middleware |
| 220,000,000 | `SchemataSessionFeature<T>` | `UseSession()` / `UseSession<T>()` | `Session` services and middleware with a pluggable `ISessionStore` |
| 230,000,000 | `SchemataControllersFeature` | `UseControllers()` | MVC controllers via `AddControllers`; maps `MapDefaultControllerRoute` endpoint |
| 240,000,000 | `SchemataJsonSerializerFeature` | `UseJsonSerializer()` | JSON serialization with snake_case naming, polymorphic type resolution, and AIP `@type` conventions |

### Notes on built-in features

`SchemataControllersFeature` has `[DependsOn<SchemataRoutingFeature>]` — calling `UseControllers()` auto-registers `SchemataRoutingFeature`.

`SchemataControllersFeature` strips every `Schemata.*` `AssemblyPart` from MVC's `ApplicationPartManager` to prevent duplicate controller discovery. To expose a controller from a `Schemata.*` assembly, register a `SchemataExtensionPart<T>` for it.

`SchemataHttpLoggingFeature` may log PII (request/response bodies). Use with care in production.

## Extension features

Extension features ship in separate packages and anchor off `Orders.Extension` (400M) with 10M strides. Bridge features use +100K or +200K offsets within a parent's slot.

```
Built-ins (Orders.Base = 100M, +10M strides; WellKnown sub-feature uses +5M):
  100M ForwardedHeaders          190M Quota
  110M DeveloperExceptionPage    200M Cors
  120M Logging                   210M Authentication
  130M HttpLogging               220M Session
  140M W3CLogging                230M Controllers
  150M Https                     240M JsonSerializer
  160M Tenancy                   (extension slot occupying built-in range)
  170M CookiePolicy
  180M Routing
  +5M  WellKnown                 (sub-feature of Routing)

Extensions (Orders.Extension = 400M, +10M strides + bridges):
  400M Security
  410M Transport.Http             (shared HTTP transport: exception handler, JSON traits)
  420M Transport.Grpc             (shared gRPC transport: AddCodeFirstGrpc, interceptor, reflection)
  430M Identity
  440M Event
  450M Authorization
        +100K Authorization.Identity (bridge later=Authorization)
  460M Mapping
  470M Scheduling
        +100K Scheduling.Event       (bridge later=Scheduling)
  480M Flow
        +100K Flow.Http              (transport)
        +200K Flow.Grpc              (transport)
        +300K Flow.Event             (bridge later=Flow)
        +400K Flow.Scheduling        (bridge later=Flow)
  490M Resource
        +100K Resource.Http          (transport)
        +200K Resource.Grpc          (transport)
  520M Modular
```

| Priority | Package | Feature class | `Use*()` method | What it registers |
| --- | --- | --- | --- | --- |
| 160,000,000 | `Schemata.Tenancy.Foundation` | `SchemataTenancyFeature<TManager, TTenant>` | `UseTenancy()` | Tenant manager, context accessor, service scope factory, provider cache, request middleware. **Order is overridden to 900,000,000** so services register last while middleware runs early. |
| 400,000,000 | `Schemata.Security.Foundation` | `SchemataSecurityFeature` | `UseSecurity()` | Default `IAccessProvider<,>` and `IEntitlementProvider<,>` open-generic fallbacks |
| 410,000,000 | `Schemata.Transport.Http` | `SchemataTransportHttpFeature` | _(auto-pulled)_ | AIP-193 exception-handler middleware, `SchemataJsonTraits` (snake_case, long-as-string) applied to MVC and minimal-API JSON options |
| 420,000,000 | `Schemata.Transport.Grpc` | `SchemataTransportGrpcFeature` | _(auto-pulled)_ | `AddCodeFirstGrpc` with `ExceptionMappingInterceptor`, `RuntimeTypeModel.Default` traits, gRPC server reflection |
| 430,000,000 | `Schemata.Identity.Foundation` | `SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>` | `UseIdentity()` | ASP.NET Core Identity with bearer-token authentication, composite auth handler, user/role stores |
| 440,000,000 | `Schemata.Event.Foundation` | `SchemataEventFeature` | `UseEvent()` | Event bus, type registry, producer/consumer builders |
| 450,000,000 | `Schemata.Authorization.Foundation` | `SchemataAuthorizationFeature<TApplication, TAuthorization, TScope, TToken>` | `UseAuthorization()` | Authorization server core, token validation, entity stores, advisors |
| 450,100,000 | `Schemata.Authorization.Foundation` | `SchemataAuthorizationIdentityFeature` | _(auto-bridge)_ | Identity integration bridge for the authorization server |
| 460,000,000 | `Schemata.Mapping.Foundation` | `SchemataMappingFeature<T>` | `UseMapping()` | `ISimpleMapper` implementation as a scoped service |
| 470,000,000 | `Schemata.Scheduling.Foundation` | `SchemataSchedulingFeature` | `UseScheduling()` | `IScheduler`, job registration, persistence advisors |
| 470,100,000 | `Schemata.Scheduling.Event` | `SchemataSchedulingEventFeature` | `UseSchedulingEvent()` | `EventPublishingJobLifecycleObserver` bridge |
| 480,000,000 | `Schemata.Flow.Foundation` | `SchemataFlowFeature` | `UseFlow()` | BPMN process engine, state machine (default engine), process registry |
| 480,100,000 | `Schemata.Flow.Http` | `SchemataFlowHttpFeature` | `UseFlowHttp()` | `ProcessController` exposed via `SchemataExtensionPart<SchemataFlowHttpFeature>` |
| 480,200,000 | `Schemata.Flow.Grpc` | `SchemataFlowGrpcFeature` | `UseFlowGrpc()` | `ProcessService` registered via `endpoints.MapGrpcService<ProcessService>()` |
| 480,300,000 | `Schemata.Flow.Event` | `SchemataFlowEventFeature` | `UseFlowEvent()` | `AdviceFlowEventTransition`, `IEventSubscriptionStore` bridge |
| 480,400,000 | `Schemata.Flow.Scheduling` | `SchemataFlowSchedulingFeature` | `UseFlowScheduling()` | `AdviceFlowTimerTransition`, `FlowTimerJob` bridge |
| 490,000,000 | `Schemata.Resource.Foundation` | `SchemataResourceFeature` | `UseResource()` | Resource advisor pipeline, auto-discovered `[Resource]` entities |
| 490,100,000 | `Schemata.Resource.Http` | `SchemataHttpResourceFeature` | `.MapHttp()` on `SchemataResourceBuilder` | Dynamic MVC controller generation via `ResourceControllerFeatureProvider` |
| 490,200,000 | `Schemata.Resource.Grpc` | `SchemataGrpcResourceFeature` | `.MapGrpc()` on `SchemataResourceBuilder` | Code-first gRPC services via protobuf-net |
| 520,000,000 | `Schemata.Modular` | `SchemataModulesFeature<TProvider, TRunner>` | `UseModular()` | Module discovery via `IModulesProvider`, lifecycle via `IModulesRunner` |

## Activation pattern

Features are activated through `Use*()` extension methods on `SchemataBuilder`. Because features are sorted by `Priority` at startup, the call order of `Use*()` is irrelevant — the pipeline is always deterministic:

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseForwardedHeaders();
        schema.UseLogging();
        schema.UseHttps();
        schema.UseRouting();
        schema.UseCors();
        schema.UseAuthentication(auth => auth.AddJwtBearer());
        schema.UseControllers();
        schema.UseJsonSerializer();
    });
```

Some extension features return a sub-builder for further configuration:

```csharp
builder.UseSchemata(schema => {
    schema.UseResource()
          .MapHttp()
          .MapGrpc();
});
```

## Feature dependencies

Key dependency relationships declared via `[DependsOn<T>]` or `[DependsOn("type.name")]`:

| Feature | Depends on |
| --- | --- |
| `SchemataTransportHttpFeature` | `SchemataDeveloperExceptionPageFeature`, `SchemataControllersFeature`, `SchemataJsonSerializerFeature` |
| `SchemataTransportGrpcFeature` | `SchemataRoutingFeature` |
| `SchemataSessionFeature<T>` | `SchemataCookiePolicyFeature` |
| `SchemataControllersFeature` | `SchemataRoutingFeature` |
| `SchemataIdentityFeature` | `SchemataAuthenticationFeature`, `SchemataControllersFeature`, `SchemataTransportHttpFeature` |
| `SchemataAuthorizationFeature` | `SchemataControllersFeature`, `SchemataTransportHttpFeature` |
| `SchemataEventFeature` | _(none — pulled by `Schemata.Flow.Event`, `Schemata.Scheduling.Event` when configured)_ |
| `SchemataSchedulingFeature` | optional EF Core / LinqToDB entity features |
| `SchemataFlowFeature` | _(none)_ |
| `SchemataFlowEventFeature` | `SchemataFlowFeature`, `SchemataEventFeature` |
| `SchemataFlowSchedulingFeature` | `SchemataFlowFeature`, `SchemataSchedulingFeature` |
| `SchemataFlowHttpFeature` | `SchemataFlowFeature`, `SchemataTransportHttpFeature` |
| `SchemataFlowGrpcFeature` | `SchemataFlowFeature`, `SchemataTransportGrpcFeature` |
| `SchemataResourceFeature` | `SchemataRoutingFeature`, `SchemataMappingFeature`, `SchemataSecurityFeature` |
| `SchemataHttpResourceFeature` | `SchemataResourceFeature`, `SchemataTransportHttpFeature` |
| `SchemataGrpcResourceFeature` | `SchemataResourceFeature`, `SchemataTransportGrpcFeature` |

## Design motivation

The 10M stride between normal features leaves room for user features to insert themselves between any two built-ins without conflicting with the reserved range. Two smaller offsets are also reserved: `+5M` marks a sub-feature of a built-in (only `WellKnown` uses this today), and `+100K`, `+200K`, `+300K`, `+400K` mark bridges and transports stacked above an extension anchor. Above `Flow` (480M) this stacking yields `Flow.Http` at `+100K`, `Flow.Grpc` at `+200K`, `Flow.Event` at `+300K`, `Flow.Scheduling` at `+400K`; above `Resource` (490M) it yields `Resource.Http` at `+100K` and `Resource.Grpc` at `+200K`; above `Scheduling` (470M) and `Authorization` (450M) the only stacked bridge sits at `+100K`.

## Caveats

- `AddFeature` deduplicates by `RuntimeTypeHandle`. `SchemataSessionFeature<MyStore>` and `SchemataSessionFeature<OtherStore>` are two different features and both will be registered.
- `HasFeature(typeof(SchemataSessionFeature<>))` is the open-generic existence check — it matches any closed instantiation.
- Features added during another feature's `ConfigureServices` are picked up by `ConfigureApplication` and `ConfigureEndpoints` only if they were already in the sorted list when `Invoke` ran.

## See also

- [Feature System](feature-system.md) — `Order` vs `Priority`, `DependsOn`, lifecycle
- [Core Overview](overview.md) — startup sequence and three-bucket model
- [JSON Serialization](json-serialization.md) — `SchemataJsonSerializerFeature` details
- [Error Model](error-model.md) — `SchemataTransportHttpFeature` exception-handler middleware
