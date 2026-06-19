# Built-in Features

This is the authoritative priority table. Schemata orders its middleware pipeline and service
registration through **features** that implement `ISimpleFeature` (usually by extending
`FeatureBase`). Each feature declares a `Priority` — and optionally a separate `Order` — and is
activated by a `Use*()` extension on `SchemataBuilder`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Core` | `Features/FeatureBase.cs`, `Features/ISimpleFeature.cs` |
| `Schemata.Core` | `Features/Schemata*Feature.cs` (the built-in chain) |
| `Schemata.Abstractions` | `SchemataConstants.cs` (`Orders`) |

## Ordering model

`Priority` controls the order of `ConfigureApplication` and `ConfigureEndpoints`. `Order`
controls `ConfigureServices` and defaults to `Priority` (via `FeatureBase.Order => Priority`). A
feature overrides `Order` separately only when DI registration must land at a different position
than middleware.

`SchemataConstants.Orders` anchors the chains:

| Constant | Value | Purpose |
| --- | --- | --- |
| `Base` | 100,000,000 | Anchor for built-in core features |
| `Extension` | 400,000,000 | Anchor for extension feature chains (`Base + 300M`) |
| `Max` | 900,000,000 | Terminal anchor for features that must run last |

The range `[100_000_000, 900_000_000]` is reserved for built-in and extension features. User
features pick values outside it.

## Built-in features (Schemata.Core)

The core chain starts at `Orders.Base` (100M) with 10M strides. Each feature's `DefaultPriority`
is the previous one plus 10M, leaving 160M open for the tenancy extension and applying a +5M
sub-feature offset for `WellKnown` and a +20M gap before `CookiePolicy`.

| Priority | Feature class | `Use*()` method | Registers |
| --- | --- | --- | --- |
| 100,000,000 | `SchemataForwardedHeadersFeature` | `UseForwardedHeaders()` | `ForwardedHeaders` middleware (`XForwardedFor`, `XForwardedProto`) |
| 110,000,000 | `SchemataDeveloperExceptionPageFeature` | `UseDeveloperExceptionPage()` | Developer exception page (Development only) |
| 120,000,000 | `SchemataLoggingFeature` | `UseLogging()` | `ILoggingBuilder` services via `AddLogging` |
| 130,000,000 | `SchemataHttpLoggingFeature` | `UseHttpLogging()` | HTTP logging services and middleware |
| 140,000,000 | `SchemataW3CLoggingFeature` | `UseW3CLogging()` | W3C logging services and middleware |
| 150,000,000 | `SchemataHttpsFeature` | `UseHttps()` | `UseHsts` and `UseHttpsRedirection` (non-Development only) |
| 160,000,000 | _(Tenancy — extension, see below)_ | | |
| 170,000,000 | `SchemataCookiePolicyFeature` | `UseCookiePolicy()` | Cookie policy services and middleware |
| 180,000,000 | `SchemataRoutingFeature` | `UseRouting()` | Routing services and middleware |
| 185,000,000 | `SchemataWellKnownFeature` | `UseWellKnown()` | `/.well-known/` routes (+5M sub-feature of Routing) |
| 190,000,000 | `SchemataQuotaFeature` | `UseQuota()` | Rate limiter services and middleware |
| 200,000,000 | `SchemataCorsFeature` | `UseCors()` | CORS services and middleware |
| 210,000,000 | `SchemataAuthenticationFeature` | `UseAuthentication()` | Authentication and authorization services and middleware |
| 220,000,000 | `SchemataSessionFeature<T>` | `UseSession()` / `UseSession<T>()` | Session services and middleware with a pluggable `ISessionStore` |
| 230,000,000 | `SchemataControllersFeature` | `UseControllers()` | MVC controllers via `AddControllers`; maps `MapDefaultControllerRoute` |
| 240,000,000 | `SchemataJsonSerializerFeature` | `UseJsonSerializer()` | snake_case JSON, kebab-case enums, long-as-string |

`SchemataControllersFeature` carries `[DependsOn<SchemataRoutingFeature>]`. It also strips every
`Schemata.*` `AssemblyPart` from MVC's `ApplicationPartManager`; expose a controller from a
`Schemata.*` assembly by registering a `SchemataExtensionPart<T>`. `SchemataHttpLoggingFeature`
can log request and response bodies, so it carries PII warnings.

## Extension features

Extension features ship in separate packages and anchor off `Orders.Extension` (400M) with 10M
strides. Bridge and transport sub-features stack `+100K`, `+200K`, etc. above their parent anchor.
The tenancy feature is the exception: it occupies the 160M slot inside the core range while
registering its services at `Orders.Max`.

| Priority | Package | Feature class | `Use*()` method | Registers |
| --- | --- | --- | --- | --- |
| 160,000,000 | `Schemata.Tenancy.Foundation` | `SchemataTenancyFeature<TManager, TTenant>` | `UseTenancy()` | Tenant manager, context accessor, scope factory, provider cache, request middleware. **`Order` overridden to `Orders.Max` (900M)** so services register last while middleware runs early. |
| 400,000,000 | `Schemata.Security.Foundation` | `SchemataSecurityFeature` | `UseSecurity()` | Default `IAccessProvider<,>` and `IEntitlementProvider<,>` open-generic fallbacks |
| 410,000,000 | `Schemata.Transport.Http` | `SchemataTransportHttpFeature` | _(auto-pulled)_ | AIP-193 exception-handler middleware, `SchemataJsonTraits` applied to MVC and minimal-API JSON options |
| 420,000,000 | `Schemata.Transport.Grpc` | `SchemataTransportGrpcFeature` | _(auto-pulled)_ | `AddCodeFirstGrpc` with the exception-mapping interceptor, protobuf-net traits, gRPC reflection |
| 430,000,000 | `Schemata.Identity.Foundation` | `SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>` | `UseIdentity()` | ASP.NET Core Identity with bearer-token authentication, composite auth handler, user/role stores |
| 440,000,000 | `Schemata.Event.Foundation` | `SchemataEventFeature` | `UseEvent()` | Event bus, type registry, producer/consumer builders |
| 450,000,000 | `Schemata.Authorization.Foundation` | `SchemataAuthorizationFeature<TApp, TAuth, TScope, TToken>` | `UseAuthorization()` | Authorization server core, token validation, entity stores, advisors |
| 450,100,000 | `Schemata.Authorization.Identity` | `SchemataAuthorizationIdentityFeature` | _(auto-bridge)_ | Identity integration bridge for the authorization server |
| 460,000,000 | `Schemata.Mapping.Foundation` | `SchemataMappingFeature<T>` | `UseMapping()` | `ISimpleMapper` implementation as a scoped service |
| 470,000,000 | `Schemata.Scheduling.Foundation` | `SchemataSchedulingFeature` | `UseScheduling()` | `IScheduler`, job registration, persistence advisors |
| 470,100,000 | `Schemata.Scheduling.Event` | `SchemataSchedulingEventFeature` | `UseEvent()` on the scheduling builder | Event-publishing job lifecycle observer |
| 480,000,000 | `Schemata.Flow.Foundation` | `SchemataFlowFeature` | `UseFlow()` | State-machine process engine, process registry |
| 480,100,000 | `Schemata.Flow.Http` | `SchemataFlowHttpFeature` | `MapHttp()` on the flow builder | `ProcessDefinitionsController` (definition listing); process execution rides the resource pipeline as custom methods on `SchemataProcess` |
| 480,200,000 | `Schemata.Flow.Grpc` | `SchemataFlowGrpcFeature` | `MapGrpc()` on the flow builder | `ProcessDefinitionService` (definition listing); execution rides the resource pipeline as custom methods on `SchemataProcess` |
| 480,300,000 | `Schemata.Flow.Event` | `SchemataFlowEventFeature` | `UseEvent()` on the flow builder | Flow event-transition advisor, subscription-store bridge |
| 480,400,000 | `Schemata.Flow.Scheduling` | `SchemataFlowSchedulingFeature` | `UseScheduling()` on the flow builder | Flow timer-transition advisor, timer-job bridge |
| 490,000,000 | `Schemata.Resource.Foundation` | `SchemataResourceFeature` | `UseResource()` | Resource advisor pipeline, auto-discovered `[Resource]` entities |
| 490,100,000 | `Schemata.Resource.Http` | `SchemataHttpResourceFeature` | `.MapHttp()` on `SchemataResourceBuilder` | Dynamic MVC controller generation |
| 490,200,000 | `Schemata.Resource.Grpc` | `SchemataGrpcResourceFeature` | `.MapGrpc()` on `SchemataResourceBuilder` | Code-first gRPC services via protobuf-net |
| 520,000,000 | `Schemata.Modular` | `SchemataModulesFeature<TProvider, TRunner>` | `UseModular()` | Module discovery via `IModulesProvider`, lifecycle via `IModulesRunner` |

## Activation pattern

Because features sort by `Priority` at startup, `Use*()` call order does not change the pipeline:

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

Some extension features return a sub-builder:

```csharp
builder.UseSchemata(schema => {
    schema.UseResource()
          .MapHttp()
          .MapGrpc();
});
```

## Feature dependencies

Declared via `[DependsOn<T>]` (typed, auto-registers) or `[DependsOn("type.name")]` (string,
check-only):

| Feature | Depends on |
| --- | --- |
| `SchemataControllersFeature` | `SchemataRoutingFeature` |
| `SchemataWellKnownFeature` | `SchemataRoutingFeature` |
| `SchemataSessionFeature<T>` | `SchemataCookiePolicyFeature` |
| `SchemataTransportHttpFeature` | `SchemataDeveloperExceptionPageFeature`, `SchemataControllersFeature`, `SchemataJsonSerializerFeature` |
| `SchemataTransportGrpcFeature` | `SchemataRoutingFeature` |
| `SchemataIdentityFeature` | `SchemataAuthenticationFeature`, `SchemataTransportHttpFeature` |
| `SchemataAuthorizationFeature` | `SchemataAuthenticationFeature`, `SchemataTransportHttpFeature`, `SchemataWellKnownFeature` |
| `SchemataResourceFeature` | `SchemataRoutingFeature`, `SchemataMappingFeature<T>`, `SchemataSecurityFeature` |
| `SchemataFlowFeature` | `SchemataEventFeature` |
| `SchemataFlowHttpFeature` | `SchemataFlowFeature`, `SchemataHttpResourceFeature` |
| `SchemataFlowGrpcFeature` | `SchemataFlowFeature`, `SchemataGrpcResourceFeature` |
| `SchemataFlowEventFeature` | `SchemataFlowFeature`, `SchemataEventFeature` |
| `SchemataFlowSchedulingFeature` | `SchemataFlowFeature`, `SchemataSchedulingFeature` |
| `SchemataSchedulingEventFeature` | `SchemataSchedulingFeature`, `SchemataEventFeature` |
| `SchemataHttpResourceFeature` | `SchemataResourceFeature`, `SchemataTransportHttpFeature` |
| `SchemataGrpcResourceFeature` | `SchemataResourceFeature`, `SchemataTransportGrpcFeature` |

## Design rationale

The 10M stride leaves room for a user feature to slot between any two built-ins. Two smaller
offsets are reserved: `+5M` marks a built-in sub-feature (only `WellKnown` uses it), and the
`+100K`-step offsets stack bridges and transports above an extension anchor — `Flow.Http` at
`+100K`, `Flow.Grpc` at `+200K`, `Flow.Event` at `+300K`, `Flow.Scheduling` at `+400K` above
`Flow` (480M); `Resource.Http` at `+100K` and `Resource.Grpc` at `+200K` above `Resource` (490M).

## Caveats

- `AddFeature` deduplicates by `RuntimeTypeHandle`. `SchemataSessionFeature<MyStore>` and
  `SchemataSessionFeature<OtherStore>` both register.
- `HasFeature(typeof(SchemataSessionFeature<>))` is the open-generic check, matching any closed
  instantiation.
- A feature added during another feature's `ConfigureServices` is picked up by
  `ConfigureApplication` and `ConfigureEndpoints` only if it was in the sorted list when `Invoke`
  ran.

## See also

- [Feature System](feature-system.md) — `Order` vs `Priority`, `DependsOn`, lifecycle
- [JSON Serialization](json-serialization.md) — `SchemataJsonSerializerFeature`
- [Error Model](error-model.md) — `SchemataTransportHttpFeature` exception handler
