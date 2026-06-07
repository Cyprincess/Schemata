# Tenancy

`Schemata.Tenancy.Foundation` provides multi-tenant isolation through pluggable tenant resolution, per-tenant DI containers, and a request-scoped tenant context. The feature runs at `Priority` 160,000,000 (middleware position between `Https` at 150M and `CookiePolicy` at 170M) but its `Order` is overridden to `Orders.Max` so DI registration happens after every other feature. `SchemataTenancyMiddleware` then swaps the request's `IServiceProvidersFeature` with a tenant-scoped provider for the duration of each request.

Service implementations live in `Schemata.Tenancy.Skeleton/Services/` so contracts and runtime are usable from any host that pulls in the Skeleton package. `Schemata.Tenancy.Foundation` provides only the feature wiring, request-pipeline middleware, resolvers, and the fluent builder.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Tenancy.Skeleton` | `Entities/SchemataTenant.cs`, `Entities/SchemataTenantHost.cs` |
| `Schemata.Tenancy.Skeleton` | `ITenantResolver.cs`, `ITenantContextAccessor.cs`, `ITenantManager.cs`, `ITenantServiceScopeFactory.cs`, `ITenantServiceProviderFactory.cs`, `ITenantProviderCache.cs`, `ITenantProviderLease.cs` |
| `Schemata.Tenancy.Skeleton` | `SchemataTenancyOptions.cs` |
| `Schemata.Tenancy.Skeleton` | `Services/SchemataTenantContextAccessor.cs`, `Services/SchemataTenantManager.cs`, `Services/SchemataTenantServiceProviderFactory.cs`, `Services/SchemataTenantServiceScopeFactory.cs`, `Services/MemoryCacheTenantProviderCache.cs` |
| `Schemata.Tenancy.Skeleton` | `Services/TenantCompositeServiceProvider.cs`, `Services/CompositeScope.cs`, `Services/CompositeScopeFactory.cs`, `Services/TenantBoundContextAccessor.cs` |
| `Schemata.Tenancy.Foundation` | `Features/SchemataTenancyFeature.cs` (Priority 160M, Order `Orders.Max`) |
| `Schemata.Tenancy.Foundation` | `Middlewares/SchemataTenancyMiddleware.cs` |
| `Schemata.Tenancy.Foundation` | `Extensions/SchemataTenancyBuilderExtensions.cs` (resolver registrations) |
| `Schemata.Tenancy.Foundation` | `Extensions/SchemataTenancyBuilderOverrideExtensions.cs` (per-tenant DI overrides) |
| `Schemata.Tenancy.Foundation` | `SchemataTenancyBuilder.cs` |
| `Schemata.Tenancy.Foundation` | `Resolvers/RequestHeaderResolver.cs`, `RequestHostResolver.cs`, `RequestPathResolver.cs`, `RequestPrincipalResolver.cs`, `RequestQueryResolver.cs` |

## Mechanism walkthrough

### 1. Enable the feature

```csharp
builder.UseSchemata(schema => {
    var tenancy = schema.UseTenancy();        // SchemataTenant + default manager
    tenancy.UseHeaderResolver();              // x-tenant-id
});

schema.UseTenancy<MyTenant>();                       // custom entity, default manager
schema.UseTenancy<MyTenantManager, MyTenant>();      // custom manager + entity
```

Every overload returns a `SchemataTenancyBuilder<TTenant>` for chaining resolver and override registrations.

### 2. What the feature registers

`SchemataTenancyFeature<TManager, TTenant>` (Priority 160,000,000, Order `Orders.Max`):

```csharp
services.AddOptions<SchemataTenancyOptions>();

services.TryAddScoped<ITenantManager<TTenant>, TManager>();

services.TryAddScoped<SchemataTenantContextAccessor<TTenant>>();
services.TryAddTransient<ITenantContextAccessor<TTenant>>(sp =>
    sp.GetRequiredService<SchemataTenantContextAccessor<TTenant>>());

services.TryAddScoped<SchemataTenantServiceScopeFactory<TTenant>>();
services.TryAddTransient<ITenantServiceScopeFactory<TTenant>>(sp =>
    sp.GetRequiredService<SchemataTenantServiceScopeFactory<TTenant>>());

services.TryAddSingleton<ITenantProviderCache, MemoryCacheTenantProviderCache>();
services.TryAddSingleton<ITenantServiceProviderFactory<TTenant>>(sp =>
    new SchemataTenantServiceProviderFactory<TTenant>(
        sp,
        sp.GetRequiredService<ITenantProviderCache>(),
        sp.GetRequiredService<IOptions<SchemataTenancyOptions>>()));
```

`ConfigureApplication` plugs `SchemataTenancyMiddleware<TTenant>` into the pipeline.

### 3. Tenant entity

`SchemataTenant` implements `IIdentifier`, `ICanonicalName`, `IDescriptive`, `IConcurrency`, and `ITimestamp`, keyed by `Guid Uid`. Canonical name pattern `tenants/{tenant}`, table `SchemataTenants`. `Hosts` is a navigation to `SchemataTenantHost` (`tenants/{tenant}/hosts/{host}`); host look-ups can be indexed at the database level.

### 4. Tenant resolution

`SchemataTenancyMiddleware` calls `ITenantContextAccessor<TTenant>.InitializeAsync(ct)` on every request. `SchemataTenantContextAccessor<TTenant>` takes a single injected `ITenantResolver`, calls `ResolveAsync`, and — if the resolver yields a tenant id — looks it up via `ITenantManager<TTenant>.FindByTenantId`. A non-null id whose tenant is missing from the manager raises `TenantResolveException`.

The fluent builder exposes five concrete resolvers:

| Method | Resolver | Source |
| --- | --- | --- |
| `UseHeaderResolver()` | `RequestHeaderResolver` | `x-tenant-id` HTTP header |
| `UseHostResolver()` | `RequestHostResolver<TTenant>` | `Host` header matched against `SchemataTenantHost.Host` |
| `UsePathResolver()` | `RequestPathResolver` | `{Tenant}` route parameter |
| `UsePrincipalResolver()` | `RequestPrincipalResolver` | `Tenant` claim on the authenticated principal |
| `UseQueryResolver()` | `RequestQueryResolver` | `Tenant` query string parameter |

Each extension calls `services.TryAddScoped<ITenantResolver, X>()`. Only **one** `ITenantResolver` is active per host — the first `UseXxxResolver()` call wins, and every subsequent call is a no-op. To combine multiple sources (for example, "header overrides path"), implement a custom composite `ITenantResolver` and register it directly.

### 5. Per-tenant DI container

`SchemataTenantServiceProviderFactory<TTenant>.CreateServiceProvider(accessor)` returns an `ITenantProviderLease`. The factory keys the cached container by `tenant.Uid.ToString()`. Building a new container does the following:

1. Start with an empty `IServiceCollection`.
2. Register the resolved `TTenant` instance as a Singleton.
3. Register `TenantBoundContextAccessor<TTenant>` as the `ITenantContextAccessor<TTenant>` Singleton for the tenant scope; the accessor pins the tenant at construction so no HTTP-based resolution is needed.
4. Apply `SchemataTenancyOptions.TenantOverrides[id]` (per-tenant `Action<IServiceCollection>` delegates) in order.
5. Apply `SchemataTenancyOptions.DynamicOverrides` (`Action<string, IServiceCollection, IServiceProvider>`) in order.
6. Validate every newly added descriptor: only `ServiceLifetime.Singleton` is accepted; Scoped or Transient registrations throw `InvalidOperationException` at build time.
7. Build the overrides container and wrap it in a `TenantCompositeServiceProvider(overrides, root)`.

`TenantCompositeServiceProvider.GetService` resolves a service by checking the tenant overrides first and falling back to the host root, with three explicit exceptions: `IServiceScopeFactory` returns a `CompositeScopeFactory`, `IServiceProvider` returns the composite itself, and `IEnumerable<>` lookups go directly to the host root so collection registrations stay coherent.

### 6. Provider lease lifecycle

`ITenantProviderLease` is a refcounted handle over a cached provider:

```csharp
public interface ITenantProviderLease : IDisposable
{
    IServiceProvider Provider { get; }
}
```

`MemoryCacheTenantProviderCache.Lease(id, factory)` either returns a fresh lease over the existing entry (and refreshes LRU position) or builds a new provider via `factory()`. The cache holds at most `SchemataTenancyOptions.ProviderMaxCapacity` entries (default 1000); entries whose `LastAccess` is older than `SchemataTenancyOptions.ProviderSlidingExpiration` (default 30 minutes) are evicted on the next access. Eviction and explicit `Remove(id)` mark the entry as retired; the actual disposal of the underlying provider is deferred until every outstanding lease for that entry has been released. This makes it safe for the cache to retire an entry while another request is still using a scope built from it.

### 7. Tenant service scopes

`SchemataTenantServiceScopeFactory<TTenant>.CreateScope()`:

```csharp
if (_accessor.Tenant is null) {
    return _root is IServiceScope existing ? existing : _root.CreateScope();
}

var lease = _factory.CreateServiceProvider(_accessor);
try {
    var inner = lease.Provider.CreateScope();   // CompositeScope over a fresh host scope
    return new LeasedTenantScope(inner, lease); // disposes inner then releases the lease
} catch {
    lease.Dispose();
    throw;
}
```

`CompositeScope` delegates Scoped and Transient resolution to a host `IServiceScope`, while keeping the tenant overrides container visible at the top of the lookup chain. The disposal sequence is fixed: the inner host scope is disposed first (so its scoped services release any resources), then the lease is released (so the cached singleton container can be retired when no lease remains).

## Per-tenant DI overrides

`SchemataTenancyOptions.TenantOverrides` and `DynamicOverrides` are populated through `SchemataTenancyBuilderOverrideExtensions`. Both produce Singleton-only registrations that are layered into the per-tenant container at build time. Scoped or Transient registrations are rejected — tenant-aware services that need to participate in the per-request injection chain must consult `ITenantContextAccessor<TTenant>` at call time instead.

## Extension points

| Interface | Purpose |
| --- | --- |
| `ITenantResolver` | Add a custom resolution strategy. Register via `services.TryAddEnumerable`. |
| `ITenantManager<TTenant>` | Replace the default Repository-backed manager (e.g. cache, remote service). |
| `ITenantServiceProviderFactory<TTenant>` | Replace the lease-based factory entirely. |
| `ITenantProviderCache` | Plug in a distributed or out-of-process cache; preserve lease semantics. |
| `SchemataTenancyOptions` | Tune capacity, sliding expiration, and per-tenant overrides. |

## Caveats

- `SchemataTenancyFeature` has `Priority = 160,000,000` and `Order = Orders.Max`. The unusual split is intentional: middleware ordering stays low while DI registration runs last.
- Tenant overrides must be Singleton. Scoped or Transient registrations throw `InvalidOperationException` at provider-build time with the offending service type in the message.
- `ITenantContextAccessor<TTenant>` inside a per-tenant scope is bound by `TenantBoundContextAccessor<TTenant>`; calling `InitializeAsync` on it is a no-op because the tenant is fixed for the lifetime of the scope.
- Cache eviction is deferred until the last outstanding lease is released. A tenant whose container is removed mid-request keeps serving that request until the scope completes.
- `IEnumerable<>` resolutions on the composite provider always go to the host root. Per-tenant overrides cannot replace a host-provided collection registration wholesale; they can only add new bindings for the same service type via `TryAddEnumerable` inside the override delegate, which the host root will not see.

## See also

- [Built-in Features](core/built-in-features.md) — feature priority table
- [Entity Traits](entity/traits.md) — `IIdentifier`, `ICanonicalName`, `IDescriptive`, `IConcurrency`, `ITimestamp`
- [Multi-Tenant Setup](../cookbook/multi-tenant-cookbook.md) — combined resolvers and per-tenant DI overrides
