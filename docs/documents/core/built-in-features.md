# Built-in Features

Schemata organizes its middleware pipeline and service registration through **features** -- classes that implement `ISimpleFeature` (typically by extending `FeatureBase`). Each feature declares a `Priority` (and optionally a separate `Order`) that determines the order in which it configures middleware and registers services. Features are activated on the `SchemataBuilder` via `UseXxx()` extension methods.

## Ordering model

Every feature exposes two ordering properties:

- **`Priority`** -- controls the order in which `ConfigureApplication` and `ConfigureEndpoints` are called.
- **`Order`** -- defaults to `Priority` unless overridden; controls the order in which `ConfigureServices` is called, allowing a feature to register services at one position and middleware at another.

Schemata reserves the range **[100,000,000 .. 900,000,000]** for its own features:

| Constant                             | Value       | Purpose                                         |
| ------------------------------------ | ----------- | ----------------------------------------------- |
| `SchemataConstants.Orders.Base`      | 100,000,000 | Anchor for the built-in feature chain           |
| `SchemataConstants.Orders.Extension` | 400,000,000 | Anchor for extension feature chains             |
| `SchemataConstants.Orders.Max`       | 900,000,000 | Terminal anchor for features that must run last |

Application-level features should use priorities **below** `Base` (to run before built-ins) or **above** `Max` (to run after everything).

## Built-in features (Schemata.Core)

These features ship in the `Schemata.Core` package and cover the fundamental ASP.NET Core middleware pipeline.

| Priority    | Feature class                           | `UseXxx()` method                                    | Registers                                                                                           |
| ----------- | --------------------------------------- | ---------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| 100,000,000 | `SchemataForwardedHeadersFeature`       | `UseForwardedHeaders()`                              | `ForwardedHeaders` middleware (`XForwardedFor`, `XForwardedProto`)                                  |
| 110,000,000 | `SchemataDeveloperExceptionPageFeature` | `UseDeveloperExceptionPage()`                        | `DeveloperExceptionPage` middleware (Development only)                                              |
| 120,000,000 | `SchemataExceptionHandlerFeature`       | _(auto, depends on `SchemataJsonSerializerFeature`)_ | `ExceptionHandler` middleware; converts `SchemataException` to structured JSON error responses      |
| 130,000,000 | `SchemataLoggingFeature`                | `UseLogging()`                                       | `ILoggingBuilder` services via `AddLogging`                                                         |
| 140,000,000 | `SchemataHttpLoggingFeature`            | `UseHttpLogging()`                                   | `HttpLogging` services and middleware                                                               |
| 150,000,000 | `SchemataW3CLoggingFeature`             | `UseW3CLogging()`                                    | `W3CLogging` services and middleware                                                                |
| 160,000,000 | `SchemataHttpsFeature`                  | `UseHttps()`                                         | `UseHsts` and `UseHttpsRedirection` middleware (non-Development only)                               |
| 180,000,000 | `SchemataCookiePolicyFeature`           | `UseCookiePolicy()`                                  | `CookiePolicy` services and middleware                                                              |
| 190,000,000 | `SchemataRoutingFeature`                | `UseRouting()`                                       | `Routing` services and middleware                                                                   |
| 200,000,000 | `SchemataQuotaFeature`                  | `UseQuota()`                                         | `RateLimiter` services and middleware; wraps rejection in structured error responses                |
| 210,000,000 | `SchemataCorsFeature`                   | `UseCors()`                                          | `CORS` services and middleware                                                                      |
| 220,000,000 | `SchemataAuthenticationFeature`         | `UseAuthentication()`                                | `Authentication` and `Authorization` services and middleware                                        |
| 230,000,000 | `SchemataSessionFeature<T>`             | `UseSession()` / `UseSession<T>()`                   | `Session` services and middleware with a pluggable `ISessionStore`                                  |
| 240,000,000 | `SchemataControllersFeature`            | `UseControllers()`                                   | MVC controllers via `AddControllers`; maps `MapDefaultControllerRoute` endpoint                     |
| 250,000,000 | `SchemataJsonSerializerFeature`         | `UseJsonSerializer()`                                | JSON serialization with snake_case naming, polymorphic type resolution, and AIP `@type` conventions |

### Priority gap at 170,000,000

There is a 20,000,000 gap between `SchemataHttpsFeature` (160,000,000) and `SchemataCookiePolicyFeature` (180,000,000). The `SchemataTenancyFeature` extension inserts itself at 170,000,000 in that gap (see extension features below).

## Extension features

These features ship in separate packages and extend the pipeline with higher-level capabilities. They anchor off `SchemataConstants.Orders.Extension` (400,000,000) or chain from other features.

| Priority    | Package                             | Feature class                                                                | `UseXxx()` method                         | Registers                                                                                                                                                                                                                                                                   |
| ----------- | ----------------------------------- | ---------------------------------------------------------------------------- | ----------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 170,000,000 | `Schemata.Tenancy.Foundation`       | `SchemataTenancyFeature<TManager, TTenant, TKey>`                            | `UseTenancy()`                            | Tenant manager, context accessor, service scope factory, and two middleware components (`SchemataTenantContextAccessorInitializer`, `SchemataTenantServiceProviderReplacer`). **Order is overridden to 900,000,000** so services register last while middleware runs early. |
| 400,000,000 | `Schemata.Security.Foundation`      | `SchemataSecurityFeature`                                                    | `UseSecurity()`                           | Default `IAccessProvider<,>` and `IEntitlementProvider<,>` open-generic fallbacks                                                                                                                                                                                           |
| 410,000,000 | `Schemata.Identity.Foundation`      | `SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>`              | `UseIdentity()`                           | ASP.NET Core Identity with bearer-token authentication, composite auth handler, user/role stores, mail/message sender fallbacks                                                                                                                                             |
| 420,000,000 | `Schemata.Authorization.Foundation` | `SchemataAuthorizationFeature<TApplication, TAuthorization, TScope, TToken>` | `UseAuthorization()`                      | Authorization server core, token validation, entity stores, model binders, advisors; ASP.NET Core integration                                                                                                                                                               |
| 430,000,000 | `Schemata.Mapping.Foundation`       | `SchemataMappingFeature<T>`                                                  | `UseMapping()`                            | `ISimpleMapper` implementation as a scoped service                                                                                                                                                                                                                          |
| 440,000,000 | `Schemata.Workflow.Foundation`      | `SchemataWorkflowFeature<TWorkflow, TTransition, TResponse>`                 | `UseWorkflow()`                           | Workflow manager, type resolver, mapping configuration, and MVC extension parts                                                                                                                                                                                             |
| 450,000,000 | `Schemata.Resource.Foundation`      | `SchemataResourceFeature`                                                    | `UseResource()`                           | Resource advisor pipeline (validation, freshness, idempotency advisors backed by `ICacheProvider`), auto-discovered `[Resource]` entities                                                                                                                                         |
| 460,000,000 | `Schemata.Resource.Http`            | `SchemataHttpResourceFeature`                                                | `.MapHttp()` on `SchemataResourceBuilder` | Dynamic MVC controller generation for resources via `ResourceControllerFeatureProvider` and `ResourceControllerConvention`                                                                                                                                                  |
| 470,000,000 | `Schemata.Resource.Grpc`            | `SchemataGrpcResourceFeature`                                                | `.MapGrpc()` on `SchemataResourceBuilder` | Code-first gRPC services via protobuf-net, `ExceptionMappingInterceptor`, gRPC reflection, per-resource `MapGrpcService` endpoints                                                                                                                                          |
| 480,000,000 | `Schemata.Modular`                  | `SchemataModulesFeature<TProvider, TRunner>`                                 | `UseModular()`                            | Module discovery via `IModulesProvider`, lifecycle via `IModulesRunner`; delegates `ConfigureServices`, `ConfigureApplication`, and `ConfigureEndpoints` to the runner                                                                                                      |

## Activation pattern

Features are activated through `UseXxx()` extension methods on `SchemataBuilder`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.UseSchemata(schema => {
    schema.UseForwardedHeaders();
    schema.UseDeveloperExceptionPage();
    schema.UseLogging();
    schema.UseHttps();
    schema.UseCookiePolicy();
    schema.UseRouting();
    schema.UseQuota();
    schema.UseCors();
    schema.UseAuthentication(auth => auth.AddJwtBearer());
    schema.UseSession();
    schema.UseControllers();
    schema.UseJsonSerializer();
});
```

Features are sorted by `Priority` at startup, so the call order of `UseXxx()` is irrelevant. This means the pipeline is always deterministic regardless of call order.

### Chained builders

Some extension features return a sub-builder for further configuration:

```csharp
builder.UseSchemata(schema => {
    schema.UseResource()
          .MapHttp()
          .MapGrpc();

    schema.UseAuthorization()
          .UseCodeFlow()
          .UseRefreshTokenFlow()
          .UseClientCredentialsFlow()
          .UseIntrospection()
          .UseRevocation()
          .UseEndSession();
});
```

## Feature dependencies

Features can declare dependencies using `[DependsOn("fully.qualified.type.name")]`. When a feature depends on another, the framework ensures the dependency is registered. Key dependency relationships:

| Feature                           | Depends on                                                                    |
| --------------------------------- | ----------------------------------------------------------------------------- |
| `SchemataExceptionHandlerFeature` | `SchemataJsonSerializerFeature`                                               |
| `SchemataSessionFeature<T>`       | `SchemataCookiePolicyFeature`                                                 |
| `SchemataControllersFeature`      | `SchemataRoutingFeature`, `SchemataExceptionHandlerFeature`                   |
| `SchemataIdentityFeature`         | `SchemataAuthenticationFeature`, `SchemataControllersFeature`                 |
| `SchemataAuthorizationFeature`    | `SchemataControllersFeature`                                                  |
| `SchemataWorkflowFeature`         | `SchemataControllersFeature`, `SchemataSecurityFeature`                       |
| `SchemataResourceFeature`         | `SchemataRoutingFeature`, `SchemataMappingFeature`, `SchemataSecurityFeature` |
| `SchemataHttpResourceFeature`     | `SchemataControllersFeature`, `SchemataResourceFeature`                       |
| `SchemataGrpcResourceFeature`     | `SchemataResourceFeature`                                                     |
