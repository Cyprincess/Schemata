# Tenancy

`Schemata.Tenancy.Foundation` isolates requests by tenant through pluggable resolution, a
request-scoped tenant context, and an optional per-tenant DI container. The feature runs at
`Priority = SchemataCorsFeature.DefaultPriority + 5_000_000 = 205_000_000` — after routing and CORS,
before authentication — so routing matches `{Tenant}` route values before the path resolver runs.
`Order = Orders.Max = 900_000_000`, so its DI registration runs after every other feature. On each
request, `SchemataTenancyMiddleware` resolves the tenant, then swaps the request's
`IServiceProvidersFeature` for a tenant-scoped provider.

Contracts and the runtime services live in `Schemata.Tenancy.Skeleton`; `Schemata.Tenancy.Foundation`
adds the feature, middlewares, resolvers, and the fluent builder.

## Where the code lives

| Package                       | Key files                                                                                                                                                                                                                                                                                        |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Schemata.Tenancy.Skeleton`   | `Entities/SchemataTenant.cs`, `Entities/SchemataTenantHost.cs`                                                                                                                                                                                                                                   |
| `Schemata.Tenancy.Skeleton`   | `ITenantResolver.cs`, `ITenantContextAccessor.cs`, `ITenantContextInitializer.cs`, `ITenantManager.cs`, `ITenantServiceScopeFactory.cs`, `ITenantServiceProviderFactory.cs`, `ITenantProviderCache.cs`, `ITenantProviderLease.cs`, `TenantResolutionStage.cs`, `SchemataTenancyOptions.cs` |
| `Schemata.Tenancy.Foundation` | `Services/` — `SchemataTenantContextAccessor`, `SchemataTenantManager`, `SchemataTenantServiceProviderFactory`, `SchemataTenantServiceScopeFactory`, `MemoryCacheTenantProviderCache`, `TenantCompositeServiceProvider`, `CompositeScope`, `CompositeScopeFactory`, `TenantBoundContextAccessor` |
| `Schemata.Tenancy.Foundation` | `Handlers/` — `CreateTenantHandler`, `UpdateTenantHandler`, `DeleteTenantHandler`, `SetTenantDisplayNameHandler`, `SetTenantLocalizedDisplayNamesHandler`, `SetTenantHostsHandler`, `FindTenantByIdHandler`, `FindTenantByHostHandler`, `GetTenantHostsHandler` |
| `Schemata.Tenancy.Foundation` | `Features/SchemataTenancyFeature.cs`, `Features/SchemataTenantPrincipalFeature.cs`, `Features/SchemataTenantExecutionFeature.cs`, `Middlewares/SchemataTenancyMiddleware.cs`, `SchemataTenancyBuilder.cs` |
| `Schemata.Tenancy.Foundation` | `Extensions/SchemataTenancyBuilderExtensions.cs` (resolvers), `Extensions/SchemataTenancyBuilderOverrideExtensions.cs` (overrides) |
| `Schemata.Tenancy.Foundation` | `Resolvers/Request{Header,Host,Path,Principal,Query}Resolver.cs`, `Resolvers/TenantId.cs` |
| `Schemata.Tenancy.Messaging`  | `TenantMessageExecutionScopeFactory.cs`, `TenantMessagingExtensions.cs` |
| `Schemata.Tenancy.Caching`    | `TenantCacheProvider.cs`, `TenantCacheKey.cs`, `Extensions/TenantCacheExtensions.cs` |

Tenant Update, invariant display-name and localized display-name handlers resolve fresh repositories
from the current operation's service provider for every public manager call. Each operation owns its
unit of work and invalidates the provider cache only after an Applied mutation commits durably.
Sequential calls through one scoped manager retain that boundary without reopening a completed
externally owned repository enlistment.


## Enabling the feature

```csharp
builder.UseSchemata(schema => {
    var tenancy = schema.UseTenancy()       // SchemataTenant + SchemataTenantManager
                     .UseHeaderResolver(); // x-tenant-id
});

schema.UseTenancy<MyTenant>();                  // custom entity, default manager
schema.UseTenancy<MyTenantManager, MyTenant>(); // custom manager + entity
```

Every overload returns a `SchemataTenancyBuilder<TTenant>` for chaining resolver and override
registrations. Constraints: `TTenant : SchemataTenant` and, for the three-argument form,
`TManager : class, ITenantManager<TTenant>`. The builder registers three features:
`SchemataTenancyFeature<TManager, TTenant>` (request-stage resolution and per-request scope
binding), `SchemataTenantPrincipalFeature<TTenant>` (Principal-stage resolver invocation after
authentication), and `SchemataTenantExecutionFeature` (`TenantExecutionMiddleware` that enters
a `TenantContext` frame for the rest of the pipeline).

`SchemataTenancyFeature<TManager, TTenant>` has `Priority = SchemataCorsFeature.DefaultPriority +
5_000_000 = 205_000_000` and `Order = Orders.Max = 900_000_000`. `ConfigureServices` registers the
nine closed handlers (one per `ITenantManager<TTenant>` operation), decorates `IPolicyEvaluator`
with `TenantPolicyEvaluator`, decorates `IControllerFactory` with `TenantControllerFactory`, and
adds the MVC resource filter `TenantExecutionFilter` so each resource execution enters a
`TenantContext` frame. The exact responsibilities:

| Block                                                                                         | Source                                                                                              |
| --------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| `AddOptions<SchemataTenancyOptions>()` and MVC filter registration                             | `SchemataTenancyFeature.ConfigureServices` (lines 52-53)                                            |
| `Decorate<IPolicyEvaluator>` / `Decorate<IControllerFactory>` for tenancy-aware MVC behavior   | `SchemataTenancyFeature.ConfigureServices` (lines 54-55)                                            |
| Nine `TryAddScoped<…Handler<TTenant>>()` registrations                                         | `SchemataTenancyFeature.ConfigureServices` (lines 56-64)                                            |
| `TryAddScoped<ITenantManager<TTenant>, TManager>()` and accessor + interface alias resolution | `SchemataTenancyFeature.ConfigureServices` (lines 66-70)                                            |
| `TryAddSingleton<ITenantServiceScopeFactory>`, `ITenantProviderCache`, `ITenantServiceProviderFactory` | `SchemataTenancyFeature.ConfigureServices` (lines 72-76)                                       |

`SchemataTenantContextAccessor<TTenant>` implements both `ITenantContextAccessor<TTenant>` (read)
and `ITenantContextInitializer<TTenant>` (write); the two interfaces resolve to the same scoped
instance. The accessor takes an `IEnumerable<ITenantResolver>` — multiple resolvers participate
together, not a single one. `ConfigureApplication` adds `SchemataTenancyMiddleware<TTenant>` at priority 205_000_000
(`SchemataCorsFeature.DefaultPriority + 5_000_000`), `SchemataTenantPrincipalMiddleware<TTenant>`
at 211_000_000 (`SchemataAuthenticationFeature.DefaultPriority + 1_000_000`), and
`TenantExecutionMiddleware` at 213_000_000 (`SchemataAuthorizationFeature.DefaultPriority +
1_000_000`; `SchemataAuthorizationFeature.DefaultPriority = SchemataAuthenticationFeature.
DefaultPriority + 2_000_000`).

## Context accessor and initializer

The read and write sides are split across two interfaces. The skeleton `ITenantContextInitializer`
exposes only the public-facing entry points; the stage-aware overload lives on the concrete
`SchemataTenantContextAccessor<TTenant>` that the middleware resolves from DI:

```csharp
public interface ITenantContextAccessor<TTenant> where TTenant : SchemataTenant {
    TTenant? Tenant { get; }
    Task<IServiceProvider> GetBaseServiceProviderAsync(CancellationToken ct);
}

public interface ITenantContextInitializer<TTenant> where TTenant : SchemataTenant {
    Task InitializeAsync(CancellationToken ct);                       // resolve via Request-stage resolvers
    Task InitializeAsync(TTenant tenant, CancellationToken ct);       // bind an explicit tenant
}

public enum TenantResolutionStage { Request, Principal }

public sealed class SchemataTenantContextAccessor<TTenant>
    : ITenantContextAccessor<TTenant>, ITenantContextInitializer<TTenant>
    where TTenant : SchemataTenant
{
    public Task InitializeAsync(CancellationToken ct)
        => InitializeAsync(TenantResolutionStage.Request, ct);
    public Task InitializeAsync(TenantResolutionStage stage, CancellationToken ct) { /* … */ }
    public Task InitializeAsync(TTenant tenant, CancellationToken ct) { /* … */ }
}
```

Application code injects `ITenantContextAccessor<TTenant>` and reads `Tenant`. Only the
middlewares (and code that needs to bind a tenant outside a request) call the initializer.
`Tenant` is `null` until the request is initialized.

## Tenant resolution

`SchemataTenancyMiddleware<TTenant>.Invoke` resolves
`SchemataTenantContextAccessor<TTenant>` (the concrete class that exposes the stage-aware
initializer) and calls `InitializeAsync(TenantResolutionStage.Request, ct)`. It then enters a
`TenantContext` frame, opens a tenant scope through `ITenantServiceScopeFactory<TTenant>` via
`TenantRequestBinding.OpenAsync`, sets `http.RequestServices` to the tenant scope's
`ServiceProvider`, stores the binding in `http.Features`, and restores the original in a
`finally`. `SchemataTenantPrincipalMiddleware` calls
`binding.BindPrincipalAsync(http)` after authentication, which calls
`InitializeAsync(TenantResolutionStage.Principal, ct)` and reopens the scope only if the
resulting `TenantIdentity` differs from the binding's current `Identity`.

`SchemataTenantContextAccessor<TTenant>.InitializeAsync(stage, ct)` iterates the injected
`IEnumerable<ITenantResolver>` and only invokes resolvers whose `Stage` matches the requested
stage. Resolvers that return `null` are skipped (absent header, missing claim, etc.). The
accessor tracks `selected`, starting at `Tenant?.Uid` (the value left by the previous stage
call). Each new non-null id is rejected with `TenantResolveException` if it differs from a
non-null `selected`; a non-null id with no matching tenant in
`ITenantManager<TTenant>.FindByTenantId` also raises `TenantResolveException`.

Concretely:

- Within one stage, multiple non-null answers must agree (any disagreement throws).
- Across stages, the Principal-stage call accepts a non-null id only when the request stage
  produced no tenant (so `selected` is `null`); a Principal-stage id that differs from a
  non-null request-stage tenant throws. A Principal-stage id that agrees with the request-stage
  tenant refreshes the lookup. The practical effect is that the principal resolver can promote a
  request from host-default to a tenant (the common "header-only or principal-only" API), but
  cannot override an already-resolved tenant with a different one.

`TenantRequestBinding.BindPrincipalAsync` only reopens the request scope when the resulting
`TenantIdentity` differs from the binding's current `Identity`. A matching principal id (or a
principal resolver that returns `null`) leaves the existing scope untouched; a disagreeing
principal id is rejected by the accessor before `BindPrincipalAsync` reaches its scope-reopen
branch.

`ITenantResolver` carries its own stage:

```csharp
public interface ITenantResolver {
    TenantResolutionStage Stage => TenantResolutionStage.Request;
    Task<Guid?> ResolveAsync(CancellationToken ct = default);
}
```

The fluent builder exposes five resolvers, each reading from one source:

| Method                   | Resolver                       | Stage     | Source                                                |
| ------------------------ | ------------------------------ | --------- | ----------------------------------------------------- |
| `UseHeaderResolver()`    | `RequestHeaderResolver`        | Request   | `x-tenant-id` HTTP header                             |
| `UseHostResolver()`      | `RequestHostResolver<TTenant>` | Request   | `Host` header matched via `ITenantManager.FindByHost` |
| `UsePathResolver()`      | `RequestPathResolver`          | Request   | `{Tenant}` route value                                |
| `UsePrincipalResolver()` | `RequestPrincipalResolver`     | Principal | `Tenant` claim on the principal                       |
| `UseQueryResolver()`     | `RequestQueryResolver`         | Request   | `Tenant` query-string parameter                       |

Each extension calls `services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResolver, X>())`,
so the accessor receives a collection. The accessor iterates the collection per stage call,
invoking every Request-stage resolver at request time, and invoking the Principal-stage
resolvers multiple times: once after `SchemataTenantPrincipalMiddleware` runs (post-default
authentication), and again from `TenantPolicyEvaluator.AuthenticateAsync` after each
authorization policy's authentication scheme resolves. Custom Principal-stage resolvers must be
idempotent across these calls — a second invocation that yields a different non-null id after a
matching first invocation throws `TenantResolveException`. The header, path, principal, and
query resolvers parse their value through `TenantId.Parse`, which throws
`TenantResolveException` on a malformed Guid.

## Per-tenant DI container

`SchemataTenantServiceProviderFactory<TTenant>.CreateServiceProviderAsync(identifier, ct)` returns
an `ITenantProviderLease`. `MemoryCacheTenantProviderCache.Lease(id, version, factory)` keys each
entry by `{id.Length}:{id}:{version:N}` where `version` is the tenant's `Timestamp` (Guid) — a
change in `Timestamp` forces a fresh build. Building a container:

1. Start from an empty `ServiceCollection`.
2. Register the resolved `TTenant` instance as a Singleton.
3. Register `TenantBoundContextAccessor<TTenant>` as the `ITenantContextAccessor<TTenant>` Singleton
   for the tenant scope — it pins the tenant at construction, so no per-request resolution happens
   inside the tenant container.
4. Apply `SchemataTenancyOptions.TenantOverrides[id]` (the per-tenant delegates) in order.
5. Apply `SchemataTenancyOptions.DynamicOverrides` (each `Action<string, IServiceCollection,
   IServiceProvider>`) in order.
6. Validate each added descriptor: open-generic service types or open-generic implementation types
   raise `InvalidOperationException` naming the offending service type. `Singleton`, `Scoped`, and
   `Transient` lifetimes are accepted; the composite provider resolves scoped services from the
   underlying tenant scope and transient services per-call.
7. Build the overrides container and wrap it in `TenantCompositeServiceProvider(overrides, root)`.

`TenantCompositeServiceProvider.GetService` checks the tenant overrides first and falls back to the
host root, with three exceptions: `IServiceScopeFactory` returns a `CompositeScopeFactory`,
`IServiceProvider` returns the composite itself, and `IEnumerable<>` lookups merge host-root and
tenant registrations so collection bindings stay coherent. Scoped resolutions route through
`TenantResolutionContext.Services` so the lifetime matches the per-request scope that owns the
tenant binding.

## Override registration

`ForAll` and `ForTenant` on the builder populate these registrations:

| Method                                 | Lands in                                                                     | Allowed lifetimes                                                                            |
| -------------------------------------- | ---------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| `ForAll(configure)`                    | Root `IServiceCollection` (host)                                             | Any — these are normal host services seen by every tenant via the composite's root fallback |
| `ForTenant(tenantId, configure)`       | `SchemataTenancyOptions.TenantOverrides[tenantId]`                           | Singleton, Scoped, or Transient; open-generic descriptors are rejected at provider build     |
| `ForTenant((id, services, root) => …)` | `SchemataTenancyOptions.DynamicOverrides`, applied to every tenant container | Singleton, Scoped, or Transient; open-generic descriptors are rejected at provider build     |

A tenant-aware service that must participate in the per-request scope (an `AddDbContext`, a
repository) belongs in `ForAll`. Inside the per-tenant composite provider,
`TenantCompositeServiceProvider.GetService` falls back to the host scope's provider when the
tenant overrides do not register the type — so a host-registered service receives the host
scope's `SchemataTenantContextAccessor`, not the tenant override's `TenantBoundContextAccessor`.
That host accessor is constructed in the host scope and never initialized for the current
request, so reading `accessor.Tenant` returns `null`. Host services resolve the request's tenant
identity through `TenantContext.Current` (the `TenantContext.Enter(new(accessor.Tenant?.Uid))`
frame set by `SchemataTenancyMiddleware` and refreshed by `SchemataTenantPrincipalMiddleware`
and `TenantExecutionMiddleware`). For services that need tenant-side dependencies
(registrations in the tenant override container), register them explicitly with
`ForTenant(...)` instead.

## Provider lease lifecycle

`ITenantProviderLease : IDisposable` is a refcounted handle over a cached provider, exposing a
single `Provider` property. `MemoryCacheTenantProviderCache.Lease(id, version, factory)` either
hands back a fresh lease over an existing entry (refreshing its LRU position) or builds a new
provider via `factory()`. The cache holds at most `SchemataTenancyOptions.ProviderMaxCapacity`
entries (default 1000); entries idle longer than `SchemataTenancyOptions.ProviderSlidingExpiration`
(default 30 minutes) are evicted on the next access. Eviction and explicit `Remove(id)` retire the
entry, but the underlying provider is disposed only after the last outstanding lease is released.
An entry can be retired while a request still uses a scope built from it. A lease acquired
while the entry was current but later retired keeps using its captured provider; subsequent leases
on the same id trigger a rebuild keyed by the tenant's current `Timestamp`. Host shutdown also
retires providers and defers disposal for outstanding leases.

Cleanup attempts every eligible provider before propagating failures: a single error retains its
original exception, and multiple errors produce an `AggregateException`. If expiration or capacity
cleanup fails during acquisition, the cache releases that acquisition's undelivered lease before
throwing; existing handles remain valid. A failed disposal attempt counts as the provider's one
cleanup attempt, including when the last lease is released. `ITenantProviderCache` owns container
leases; `ICacheProvider` owns cached values, with tenant key framing supplied by `TenantCacheProvider`. See
[`MemoryCacheTenantProviderCache`](../../src/Schemata.Tenancy.Foundation/Services/MemoryCacheTenantProviderCache.cs)
for acquisition compensation, retirement, and synchronous/asynchronous cleanup.

## Tenant scopes

`SchemataTenantServiceScopeFactory<TTenant>.CreateAsync(identity, ct)`: when `identity` is
`TenantIdentity.Host`, it returns a host scope. Otherwise it leases a tenant provider, creates a
scope over it (`CompositeScope` layering the tenant overrides above a fresh host scope), and wraps
the two in a `LeasedTenantScope` that disposes the inner host scope first, then releases the lease.
**The factory does not enter `TenantContext` itself** — creating the scope establishes the DI
lifetime only. Callers must synchronously `TenantContext.Enter(identity)` and retain the returned
lease for the duration of the scope: `TenantCacheKey.Frame` reads `TenantContext.Current` for
every cache key, and `MessageContexts.Capture` exports the same identity into the outgoing
`MessageContext`. A background caller that resolves a scope without entering the identity gets
the right services but the wrong ambient tenant.

## Tenant entities

`SchemataTenant` implements `IIdentifier`, `ICanonicalName`, `IDescriptive`, `IConcurrency`, and
`ITimestamp`, keyed by `Guid Uid`. Table `SchemataTenants`, canonical name `tenants/{tenant}`. Its
`Hosts` navigation links to `SchemataTenantHost` (table `SchemataTenantHosts`, canonical name
`tenants/{tenant}/hosts/{host}`); the host's `Name` is `[NotMapped]` and projects the normalized
`Host` string used by `RequestHostResolver`.

## Messaging bridge

`Schemata.Tenancy.Messaging` installs a tenant-aware `IMessageExecutionScopeFactory` so actor turns
and request dispatches that cross DI scope, thread, or process boundaries resolve to the right
tenant. The bridge is opt-in — install it only when an actor mailbox, scheduler reminder, or
messaging bridge must run inside a tenant scope:

```csharp
schema.UseTenancy<MyTenant>()
      .UseHeaderResolver()
      .UseMessaging();                       // Schemata.Tenancy.Messaging
```

`TenantMessagingExtensions.UseMessaging<TTenant>` calls
`services.Replace(ServiceDescriptor.Singleton<IMessageExecutionScopeFactory,
TenantMessageExecutionScopeFactory<TTenant>>())`, replacing the default `MessageExecutionScopeFactory`
that ships in `Schemata.Messaging.Skeleton`. The replacement:

1. Resolves the tenant identity from `MessageContexts.Identity(context)` (the `tenancy.tenant-id`
   item captured by `MessageContexts.Capture` on the sender side, or `TenantIdentity.Host`).
2. Builds a short bootstrap scope off the host root, resolves
   `ITenantServiceScopeFactory<TTenant>` from it, and asks for a tenant scope.
3. Returns a `MessageExecutionScope` wrapping the tenant scope. `Enter` installs the
   identity into `TenantContext` (the actor instance must call it and retain the lease around
   `RestoreAsync` and any service use; see the [actor overview](actor/overview.md#messagecontextscapture-the-boundary-rule)
   for the boundary example). `RestoreAsync` then iterates the registered
   `IMessageContextPropagator` collection against the tenant scope.

Without `UseMessaging`, the default factory rejects non-host identities with
`TenantResolveException`; with `UseMessaging`, actor turns resolve to the right tenant's
repositories instead of the wrong or default ones. `Actor.Foundation` depends on no tenancy type at
all — it resolves whatever `IMessageExecutionScopeFactory` is installed.

`UseMessaging` also binds the dispatcher to the same owner on both sides of the tenant boundary.
Each tenant container registers the concrete `InProcessRequestDispatcher`, so a consumed request —
the RabbitMQ consumer host, an actor turn, a scheduler job — runs its local pipeline with
tenant-scoped handlers and advisors. When the host dispatches in-process, the tenant container
additionally binds `IRequestDispatcher`, `ICommandDispatcher`, and `IQueryDispatcher` to that same
tenant instance, and a tenant consumer resolving any public interface executes with tenant
dependencies. Handler and pipeline-advisor collections compose host-first with tenant additions; two
exclusive handlers for one request type still fail dispatch with `InvalidOperationException`. When
the host's dispatcher slots belong to a transport such as the RabbitMQ dispatcher, the tenant
container binds no interface and tenant callers keep publishing through that transport — only the
inbound concrete stays tenant-bound.

## Caching bridge

`Schemata.Tenancy.Caching` selects one tenant-aware wrapper as the public cache selection over the
registered backend. The bridge is opt-in and composes in either registration order:

```csharp
services.AddMemoryCacheProvider();            // Schemata.Caching.Memory backend
services.AddTenantCache();                    // Schemata.Tenancy.Caching: selects the tenant wrapper
```

`TenantCacheExtensions.AddTenantCache` installs `TenantCacheProvider` through
`AddCacheProviderWrapper`: the wrapper becomes the public selection behind the canonical
`ICacheProvider` outlet and receives the selected backend through
`[FromKeyedServices(CacheServiceKeys.Backend)]`. Calling `AddTenantCache` before or after the
backend registration yields the same composition, and repeating it keeps one wrapper instance
without self-wrapping.

The wrapper resolves `TenantContext.Current` and frames the incoming key through
`TenantCacheKey.Frame` on every operation: tenant scope yields `{uid:N}\x1e{key}` (the framework's
record-separator convention), host scope yields `host\x1e{key}`. Cache entries from different
tenants never collide, and a host-default lookup still resolves against the host namespace. The
wrapper is pure key-framing; cache value shape, TTL, and serialization remain whatever the backend
provider enforces.

## TenantResolveException

`Schemata.Abstractions.Exceptions.TenantResolveException` (HTTP 400, gRPC `FAILED_PRECONDITION`) is
raised when a resolver reads a malformed Guid, when a resolved id has no matching tenant, when the
host resolver finds no tenant for the `Host` header, when the message-execution scope factory sees
a non-host identity without the tenancy messaging bridge installed, or when the provider factory
is asked to build a container with no bound tenant.

## Extension points

| Interface                                | Purpose                                                           |
| ---------------------------------------- | ----------------------------------------------------------------- |
| `ITenantResolver`                        | Add a resolution strategy; multiple resolvers compose per stage.  |
| `ITenantManager<TTenant>`                | Replace the dispatcher-backed manager.                            |
| `ITenantServiceProviderFactory<TTenant>` | Replace the lease-based factory.                                  |
| `ITenantProviderCache`                   | Plug in a different cache while preserving lease semantics.       |
| `IMessageExecutionScopeFactory`          | Replace the messaging execution scope factory (see messaging bridge). |
| `SchemataTenancyOptions`                 | Tune capacity, sliding expiration, and overrides.                 |

## Caveats

- The `Priority`/`Order` split is intentional: middleware ordering stays low while DI registration
  runs last.
- Tenant override descriptors must not be open generics; a `ServiceType` or implementation type
  with unclosed generic parameters raises `InvalidOperationException` at provider-build time.
- `ITenantContextAccessor<TTenant>` inside a tenant container is `TenantBoundContextAccessor`; it is
  fixed for the scope's lifetime.
- `IEnumerable<>` resolutions on the composite merge host and tenant registrations; a tenant
  override cannot replace a host collection wholesale.
- The `Schemata.Tenancy.Messaging` and `Schemata.Tenancy.Caching` packages are opt-in. Calling
  `tenancy.UseMessaging()` without `UseTenancy<TTenant>()` leaves
  `ITenantServiceScopeFactory<TTenant>` unregistered, so the bridge's first turn throws on
  `GetRequiredService`; calling `services.AddTenantCache()` without a backend provider registered
  throws an `InvalidOperationException` when the canonical `ICacheProvider` outlet is first
  resolved.

## See also

- [Multi-Tenancy guide](../guides/multi-tenancy.md) — resolution and isolation on the Student app
- [Multi-Tenant cookbook](../cookbook/multi-tenant-cookbook.md) — per-tenant data and DI overrides
- [Built-in Features](core/built-in-features.md) — feature priority table