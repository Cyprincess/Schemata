# Built-in Features

This is the authoritative priority table. Schemata orders its middleware pipeline and service
registration through **features** that implement `ISimpleFeature` (usually by extending
`FeatureBase`). Each feature declares a `Priority` — and optionally a separate `Order` — and is
activated by a `Use*()` extension on `SchemataBuilder`.

## Where the code lives

| Package                 | Key files                                               |
| ----------------------- | ------------------------------------------------------- |
| `Schemata.Core`         | `Features/FeatureBase.cs`, `Features/ISimpleFeature.cs` |
| `Schemata.Core`         | `Features/Schemata*Feature.cs` (the built-in chain)     |
| `Schemata.Abstractions` | `SchemataConstants.cs` (`Orders`)                       |

## Ordering model

`Priority` controls the order of `ConfigureApplication` and `ConfigureEndpoints`. `Order`
controls `ConfigureServices` and defaults to `Priority` (via `FeatureBase.Order => Priority`). A
feature overrides `Order` separately only when DI registration must land at a different position
than middleware.

`SchemataConstants.Orders` anchors the chains:

| Constant    | Value       | Purpose                                             |
| ----------- | ----------- | --------------------------------------------------- |
| `Base`      | 100,000,000 | Anchor for built-in core features                   |
| `Extension` | 400,000,000 | Anchor for extension feature chains (`Base + 300M`) |
| `Max`       | 900,000,000 | Terminal anchor for features that must run last     |

The range `[100_000_000, 900_000_000]` is reserved for built-in and extension features. User
features pick values outside it.

## Built-in features (Schemata.Core)

The core chain starts at `Orders.Base` (100M) with 10M strides. Each feature's `DefaultPriority`
is the previous one plus 10M, leaving 160M open for the tenancy extension and applying a +5M
sub-feature offset for `WellKnown` and a +20M gap before `CookiePolicy`.

| Priority    | Feature class                           | `Use*()` method                    | Registers                                                              |
| ----------- | --------------------------------------- | ---------------------------------- | ---------------------------------------------------------------------- |
| 100,000,000 | `SchemataForwardedHeadersFeature`       | `UseForwardedHeaders()`            | `ForwardedHeaders` middleware (`XForwardedFor`, `XForwardedProto`)     |
| 110,000,000 | `SchemataDeveloperExceptionPageFeature` | `UseDeveloperExceptionPage()`      | Developer exception page (Development only)                            |
| 120,000,000 | `SchemataLoggingFeature`                | `UseLogging()`                     | `ILoggingBuilder` services via `AddLogging`                            |
| 130,000,000 | `SchemataHttpLoggingFeature`            | `UseHttpLogging()`                 | HTTP logging services and middleware                                   |
| 140,000,000 | `SchemataW3CLoggingFeature`             | `UseW3CLogging()`                  | W3C logging services and middleware                                    |
| 150,000,000 | `SchemataHttpsFeature`                  | `UseHttps()`                       | `UseHsts` and `UseHttpsRedirection` (non-Development only)             |
| 160,000,000 | _(Tenancy — extension, see below)_      |                                    |                                                                        |
| 170,000,000 | `SchemataCookiePolicyFeature`           | `UseCookiePolicy()`                | Cookie policy services and middleware                                  |
| 180,000,000 | `SchemataRoutingFeature`                | `UseRouting()`                     | Routing services and middleware                                        |
| 185,000,000 | `SchemataWellKnownFeature`              | `UseWellKnown()`                   | `/.well-known/` routes (+5M sub-feature of Routing)                    |
| 190,000,000 | `SchemataQuotaFeature`                  | `UseQuota()`                       | Rate limiter services and middleware                                   |
| 200,000,000 | `SchemataCorsFeature`                   | `UseCors()`                        | CORS services and middleware                                           |
| 210,000,000 | `SchemataAuthenticationFeature`         | `UseAuthentication()`              | Authentication and authorization services and middleware               |
| 220,000,000 | `SchemataSessionFeature<T>`             | `UseSession()` / `UseSession<T>()` | Session services and middleware with a pluggable `ISessionStore`       |
| 230,000,000 | `SchemataControllersFeature`            | `UseControllers()`                 | MVC controllers via `AddControllers`; maps `MapDefaultControllerRoute` |
| 240,000,000 | `SchemataJsonSerializerFeature`         | `UseJsonSerializer()`              | snake_case JSON, kebab-case enums, long-as-string                      |

`SchemataControllersFeature` carries `[DependsOn<SchemataRoutingFeature>]`. It also strips every
`Schemata.*` `AssemblyPart` from MVC's `ApplicationPartManager`; expose a controller from a
`Schemata.*` assembly by registering a `SchemataExtensionPart<T>`. `SchemataHttpLoggingFeature`
can log request and response bodies, so it carries PII warnings.

## Extension features

Extension features ship in separate packages and anchor off `Orders.Extension` (400M) with 10M
strides. Bridge and transport sub-features stack `+100K`, `+200K`, etc. above their parent anchor.
The tenancy feature is the exception: it occupies the 160M slot inside the core range while
registering its services at `Orders.Max`.

| Priority    | Package                             | Feature class                                                   | `Use*()` method                           | Registers                                                                                                                                                                                 |
| ----------- | ----------------------------------- | --------------------------------------------------------------- | ----------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 160,000,000 | `Schemata.Tenancy.Foundation`       | `SchemataTenancyFeature<TManager, TTenant>`                     | `UseTenancy()`                            | Tenant manager, context accessor, scope factory, provider cache, request middleware. **`Order` overridden to `Orders.Max` (900M)** so services register last while middleware runs early. |
| 400,000,000 | `Schemata.Security.Foundation`      | `SchemataSecurityFeature`                                       | `UseSecurity()`                           | Default `IAccessProvider<,>` and `IEntitlementProvider<,>` open-generic fallbacks                                                                                                         |
| 410,000,000 | `Schemata.Transport.Http`           | `SchemataTransportHttpFeature`                                  | _(auto-pulled)_                           | AIP-193 exception-handler middleware, `SchemataJsonTraits` applied to MVC and minimal-API JSON options                                                                                    |
| 420,000,000 | `Schemata.Transport.Grpc`           | `SchemataTransportGrpcFeature`                                  | _(auto-pulled)_                           | `AddCodeFirstGrpc` with the exception-mapping interceptor, protobuf-net traits, gRPC reflection                                                                                           |
| 430,000,000 | `Schemata.Identity.Foundation`      | `SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>` | `UseIdentity()`                           | ASP.NET Core Identity with bearer-token authentication, composite auth handler, user/role stores                                                                                          |
| 440,000,000 | `Schemata.Event.Foundation`         | `SchemataEventFeature`                                          | `UseEvent()`                              | Event bus, type registry, producer/consumer builders                                                                                                                                      |
| 450,000,000 | `Schemata.Actor.Foundation`         | `SchemataActorFeature`                                          | `UseActor()` on `SchemataBuilder`          | In-process actor system with per-instance mailbox serialization                                                                                                                            |
| 450,100,000 | `Schemata.Actor.Event`              | `SchemataActorEventFeature`                                     | `UseEvent()` on the actor builder          | Bridge: actor + event-bus (+100K)                                                                                                                                                          |
| 450,200,000 | `Schemata.Actor.Scheduling`         | `SchemataActorSchedulingFeature`                                | `UseScheduling()` on the actor builder     | Bridge: actor + scheduler (+200K), durable reminders                                                                                                                                       |
| 460,000,000 | `Schemata.Authorization.Foundation` | `SchemataAuthorizationFeature<TApp, TAuth, TScope, TToken>`     | `UseAuthorization()`                      | Authorization server core, token validation, entity stores, advisors                                                                                                                      |
| 460,100,000 | `Schemata.Authorization.Identity`   | `SchemataAuthorizationIdentityFeature`                          | _(auto-bridge)_                           | Identity integration bridge for the authorization server                                                                                                                                  |
| 470,000,000 | `Schemata.Mapping.Foundation`       | `SchemataMappingFeature<T>`                                     | `UseMapping()`                            | `ISimpleMapper` implementation as a scoped service                                                                                                                                        |
| 480,000,000 | `Schemata.Scheduling.Foundation`    | `SchemataSchedulingFeature`                                     | `UseScheduling()`                         | `IScheduler`, job registration, request handlers, and the job-row write gate                                                                                                                |
| 480,100,000 | `Schemata.Scheduling.Event`         | `SchemataSchedulingEventFeature`                                | `UseEvent()` on the scheduling builder    | Event-publishing job lifecycle observer                                                                                                                                                   |
| 480,200,000 | `Schemata.Scheduling.Http`          | `SchemataSchedulingHttpFeature`                                 | `.MapHttp()` on the scheduling builder     | Scheduling HTTP transport (+200K)                                                                                                                                                          |
| 480,300,000 | `Schemata.Scheduling.Grpc`          | `SchemataSchedulingGrpcFeature`                                 | `.MapGrpc()` on the scheduling builder     | Scheduling gRPC transport (+300K)                                                                                                                                                          |
| 490,000,000 | `Schemata.Flow.Foundation`          | `SchemataFlowFeature`                                           | `UseFlow()`                               | Process engine, process registry                                                                                                                                                          |
| 490,050,000 | `Schemata.Flow.StateMachine`        | `SchemataFlowStateMachineFeature`                               | `UseStateMachine()` on the flow builder    | Default state-machine engine on Flow (+50K)                                                                                                                                               |
| 490,060,000 | `Schemata.Flow.Bpmn`                | `SchemataFlowBpmnFeature`                                       | `UseBpmn()` on the flow builder            | Full BPMN 2.0.2 engine on Flow (+60K)                                                                                                                                                      |
| 490,100,000 | `Schemata.Flow.Http`                | `SchemataFlowHttpFeature`                                       | `MapHttp()` on the flow builder           | Flow HTTP transport (+100K)                                                                                                                                                                |
| 490,200,000 | `Schemata.Flow.Grpc`                | `SchemataFlowGrpcFeature`                                       | `MapGrpc()` on the flow builder           | Flow gRPC transport (+200K)                                                                                                                                                                |
| 490,300,000 | `Schemata.Flow.Event`               | `SchemataFlowEventFeature`                                      | `UseEvent()` on the flow builder          | Bridge: Flow + Event (+300K)                                                                                                                                                              |
| 490,400,000 | `Schemata.Flow.Scheduling`          | `SchemataFlowSchedulingFeature`                                 | `UseScheduling()` on the flow builder     | Bridge: Flow + Scheduling (+400K)                                                                                                                                                          |
| 490,600,000 | `Schemata.Flow.Actor`               | `SchemataFlowActorFeature`                                      | `UseActor()` on the flow builder           | Bridge: Flow + Actor (+600K)                                                                                                                                                              |
| 500,000,000 | `Schemata.Resource.Foundation`      | `SchemataResourceFeature`                                       | `UseResource()`                           | Resource advisor pipeline; entities are registered explicitly                                                                                                                              |
| 500,100,000 | `Schemata.Resource.Http`            | `SchemataHttpResourceFeature`                                   | `.MapHttp()` on `SchemataResourceBuilder` | Dynamic MVC controller generation                                                                                                                                                         |
| 500,200,000 | `Schemata.Resource.Grpc`            | `SchemataGrpcResourceFeature`                                   | `.MapGrpc()` on `SchemataResourceBuilder` | Code-first gRPC services via protobuf-net                                                                                                                                                 |
| 510,000,000 | `Schemata.Insight.Foundation`       | `SchemataInsightFeature`                                        | `UseInsight()`                            | Federated query planning and execution over resource entities                                                                                                                              |
| 510,100,000 | `Schemata.Insight.Http`             | `SchemataInsightHttpFeature`                                    | `.MapHttp()` on the insight builder        | Insight HTTP transport (+100K)                                                                                                                                                            |
| 510,200,000 | `Schemata.Insight.Grpc`             | `SchemataInsightGrpcFeature`                                    | `.MapGrpc()` on the insight builder        | Insight gRPC transport (+200K)                                                                                                                                                            |
| 520,000,000 | `Schemata.Push.Foundation`          | `SchemataPushFeature`                                           | `UsePush()`                               | Push notification fan-out                                                                                                                                                                  |
| 520,400,000 | `Schemata.Push.Scheduling`          | `SchemataPushSchedulingFeature`                                 | `UseScheduling()` on the push builder      | Deferred push dispatch through the scheduler (+400K)                                                                                                                                       |
| 530,000,000 | `Schemata.Report.Foundation`        | `SchemataReportFeature<TReport, TSnapshot, TChunk>`             | `UseReport()`                             | Report definitions, snapshots, generation                                                                                                                                                 |
| 530,100,000 | `Schemata.Report.Http`              | `SchemataReportHttpFeature`                                     | `.MapHttp()` on the report builder         | Report HTTP transport (+100K)                                                                                                                                                             |
| 530,200,000 | `Schemata.Report.Grpc`              | `SchemataReportGrpcFeature`                                     | `.MapGrpc()` on the report builder         | Report gRPC transport (+200K)                                                                                                                                                             |
| 530,400,000 | `Schemata.Report.Scheduling`        | `SchemataReportSchedulingFeature`                               | `UseScheduling()` on the report builder    | Bridge: Report + Scheduling (+400K)                                                                                                                                                        |
| 540,000,000 | `Schemata.Modular`                  | `SchemataModulesFeature<TProvider, TRunner>`                    | `UseModular()`                            | Module discovery via `IModulesProvider`, lifecycle via `IModulesRunner`                                                                                                                   |

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

Declared via `[DependsOn<T>]` (typed, auto-registers), `[DependsOn(typeof(SomeFeature))]` (type
reference, check-only; open generics such as `typeof(SchemataMappingFeature<>)` match any closed
instantiation), or `[DependsOn("type.name")]` (string, check-only):

| Feature                          | Depends on                                                                                             |
| -------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `SchemataControllersFeature`     | `SchemataRoutingFeature`                                                                               |
| `SchemataWellKnownFeature`       | `SchemataRoutingFeature`                                                                               |
| `SchemataSessionFeature<T>`      | `SchemataCookiePolicyFeature`                                                                          |
| `SchemataTransportHttpFeature`   | `SchemataDeveloperExceptionPageFeature`, `SchemataControllersFeature`, `SchemataJsonSerializerFeature` |
| `SchemataTransportGrpcFeature`   | `SchemataRoutingFeature`                                                                               |
| `SchemataIdentityFeature`        | `SchemataAuthenticationFeature`, `SchemataTransportHttpFeature`                                        |
| `SchemataAuthorizationFeature`   | `SchemataAuthenticationFeature`, `SchemataTransportHttpFeature`, `SchemataWellKnownFeature`            |
| `SchemataResourceFeature`        | `SchemataRoutingFeature`, `SchemataMappingFeature<>`                                                 |
| `SchemataFlowFeature`            | `SchemataEventFeature`                                                                                 |
| `SchemataFlowHttpFeature`        | `SchemataFlowFeature`, `SchemataHttpResourceFeature`                                                   |
| `SchemataFlowGrpcFeature`        | `SchemataFlowFeature`, `SchemataGrpcResourceFeature`                                                   |
| `SchemataFlowEventFeature`       | `SchemataFlowFeature`, `SchemataEventFeature`                                                          |
| `SchemataFlowSchedulingFeature`  | `SchemataFlowFeature`, `SchemataSchedulingFeature`                                                     |
| `SchemataSchedulingEventFeature` | `SchemataSchedulingFeature`, `SchemataEventFeature`                                                    |
| `SchemataHttpResourceFeature`    | `SchemataResourceFeature`, `SchemataTransportHttpFeature`                                              |
| `SchemataGrpcResourceFeature`    | `SchemataResourceFeature`, `SchemataTransportGrpcFeature`                                              |

## Design rationale

The 10M stride leaves room for a user feature to slot between any two built-ins. Smaller offsets
stack above an extension anchor. Flow engine sub-features use `+50K` and `+60K`
(`Flow.StateMachine`, `Flow.Bpmn`). Domain-specific transport and bridge slots then use `+100K`,
`+200K`, and `+300K`: `Scheduling.Event` occupies `+100K`, while `Flow.Event` occupies `+300K`.
Scheduling bridges use `+400K` (`Flow.Scheduling`, `Push.Scheduling`, `Report.Scheduling`), and
`+600K` reserves the actor bridge slot (`Flow.Actor`). The `+5M` offset stays reserved for a
sub-feature of a built-in; only `WellKnown` uses it.

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
