# Tenancy

`Schemata.Tenancy.Foundation` isolates requests by tenant through pluggable resolution, a
request-scoped tenant context, and an optional per-tenant DI container. The feature runs at
`Priority` 160,000,000 — middleware position between `Https` (150M) and `CookiePolicy` (170M) — but
its `Order` is `Orders.Max`, so its DI registration runs after every other feature. On each
request, `SchemataTenancyMiddleware` resolves the tenant, then swaps the request's
`IServiceProvidersFeature` for a tenant-scoped provider.

Contracts and the runtime services live in `Schemata.Tenancy.Skeleton`; `Schemata.Tenancy.Foundation`
adds the feature, middleware, resolvers, and the fluent builder.

## Where the code lives

| Package                       | Key files                                                                                                                                                                                                                                                                                        |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Schemata.Tenancy.Skeleton`   | `Entities/SchemataTenant.cs`, `Entities/SchemataTenantHost.cs`                                                                                                                                                                                                                                   |
| `Schemata.Tenancy.Skeleton`   | `ITenantResolver.cs`, `ITenantContextAccessor.cs`, `ITenantContextInitializer.cs`, `ITenantManager.cs`, `ITenantServiceScopeFactory.cs`, `ITenantServiceProviderFactory.cs`, `ITenantProviderCache.cs`, `ITenantProviderLease.cs`, `SchemataTenancyOptions.cs`                                   |
| `Schemata.Tenancy.Skeleton`   | `Services/` — `SchemataTenantContextAccessor`, `SchemataTenantManager`, `SchemataTenantServiceProviderFactory`, `SchemataTenantServiceScopeFactory`, `MemoryCacheTenantProviderCache`, `TenantCompositeServiceProvider`, `CompositeScope`, `CompositeScopeFactory`, `TenantBoundContextAccessor` |
| `Schemata.Tenancy.Foundation` | `Features/SchemataTenancyFeature.cs`, `Middlewares/SchemataTenancyMiddleware.cs`, `SchemataTenancyBuilder.cs`                                                                                                                                                                                    |
| `Schemata.Tenancy.Foundation` | `Extensions/SchemataTenancyBuilderExtensions.cs` (resolvers), `Extensions/SchemataTenancyBuilderOverrideExtensions.cs` (overrides)                                                                                                                                                               |
| `Schemata.Tenancy.Foundation` | `Resolvers/Request{Header,Host,Path,Principal,Query}Resolver.cs`, `Resolvers/TenantId.cs`                                                                                                                                                                                                        |

## Enabling the feature

```csharp
builder.UseSchemata(schema => {
    var tenancy = schema.UseTenancy();   // SchemataTenant + SchemataTenantManager
    tenancy.UseHeaderResolver();         // x-tenant-id
});

schema.UseTenancy<MyTenant>();                  // custom entity, default manager
schema.UseTenancy<MyTenantManager, MyTenant>(); // custom manager + entity
```

Every overload returns a `SchemataTenancyBuilder<TTenant>` for chaining resolver and override
registrations. Constraints: `TTenant : SchemataTenant` and, for the three-argument form,
`TManager : class, ITenantManager<TTenant>`.

`SchemataTenancyFeature<TManager, TTenant>` has `Priority = SchemataHttpsFeature.DefaultPriority +
10_000_000 = 160,000,000` and `Order = Orders.Max = 900,000,000`. `ConfigureServices` registers:

```csharp
services.AddOptions<SchemataTenancyOptions>();
services.TryAddScoped<ITenantManager<TTenant>, TManager>();

services.TryAddScoped<SchemataTenantContextAccessor<TTenant>>();
services.TryAddTransient<ITenantContextAccessor<TTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<TTenant>>());
services.TryAddTransient<ITenantContextInitializer<TTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<TTenant>>());

services.TryAddScoped<SchemataTenantServiceScopeFactory<TTenant>>();
services.TryAddTransient<ITenantServiceScopeFactory<TTenant>>(sp => sp.GetRequiredService<SchemataTenantServiceScopeFactory<TTenant>>());

services.TryAddSingleton<ITenantProviderCache, MemoryCacheTenantProviderCache>();
services.TryAddSingleton<ITenantServiceProviderFactory<TTenant>>(/* factory */);
```

`SchemataTenantContextAccessor<TTenant>` implements both `ITenantContextAccessor<TTenant>` (read)
and `ITenantContextInitializer<TTenant>` (write); the two interfaces resolve to the same scoped
instance. `ConfigureApplication` adds `SchemataTenancyMiddleware<TTenant>`.

## Context accessor and initializer

The read and write sides are split across two interfaces:

```csharp
public interface ITenantContextAccessor<TTenant> where TTenant : SchemataTenant {
    TTenant? Tenant { get; }
    Task<IServiceProvider> GetBaseServiceProviderAsync(CancellationToken ct);
}

public interface ITenantContextInitializer<TTenant> where TTenant : SchemataTenant {
    Task InitializeAsync(CancellationToken ct);          // resolve via ITenantResolver
    Task InitializeAsync(TTenant tenant, CancellationToken ct);  // bind an explicit tenant
}
```

Application code injects `ITenantContextAccessor<TTenant>` and reads `Tenant`. Only the middleware
(and code that needs to bind a tenant outside a request) calls the initializer. `Tenant` is `null`
until the request is initialized.

## Tenant resolution

`SchemataTenancyMiddleware<TTenant>.Invoke` calls
`ITenantContextInitializer<TTenant>.InitializeAsync(ct)`, then installs a `RequestServicesFeature`
backed by `ITenantServiceScopeFactory<TTenant>` as the request's `IServiceProvidersFeature` for the
duration of the request, restoring the original in a `finally`.

`SchemataTenantContextAccessor<TTenant>.InitializeAsync` takes a single injected `ITenantResolver`,
calls `ResolveAsync`, and — if it yields an id — looks the tenant up through
`ITenantManager<TTenant>.FindByTenantId`. A non-null id with no matching tenant raises
`TenantResolveException`.

`ITenantResolver` returns `Task<Guid?>`:

```csharp
public interface ITenantResolver {
    Task<Guid?> ResolveAsync(CancellationToken ct = default);
}
```

The fluent builder exposes five resolvers, each reading from one source:

| Method                   | Resolver                       | Source                                                |
| ------------------------ | ------------------------------ | ----------------------------------------------------- |
| `UseHeaderResolver()`    | `RequestHeaderResolver`        | `x-tenant-id` HTTP header                             |
| `UseHostResolver()`      | `RequestHostResolver<TTenant>` | `Host` header matched via `ITenantManager.FindByHost` |
| `UsePathResolver()`      | `RequestPathResolver`          | `{Tenant}` route value                                |
| `UsePrincipalResolver()` | `RequestPrincipalResolver`     | `Tenant` claim on the principal                       |
| `UseQueryResolver()`     | `RequestQueryResolver`         | `Tenant` query-string parameter                       |

Each extension calls `services.TryAddScoped<ITenantResolver, X>()`, and the accessor injects a
single `ITenantResolver`. Only one resolver is active per host: the first `UseXxxResolver()` wins
and the rest are no-ops. To combine sources, implement a composite `ITenantResolver` and register it
directly. The header, path, principal, and query resolvers parse their value through
`TenantId.Parse`, which throws `TenantResolveException` on a malformed Guid.

## Per-tenant DI container

`SchemataTenantServiceProviderFactory<TTenant>.CreateServiceProvider(accessor)` returns an
`ITenantProviderLease`, keyed by `tenant.Uid.ToString()`. Building a container:

1. Start from an empty `ServiceCollection`.
2. Register the resolved `TTenant` instance as a Singleton.
3. Register `TenantBoundContextAccessor<TTenant>` as the `ITenantContextAccessor<TTenant>` Singleton
   for the tenant scope — it pins the tenant at construction, so no per-request resolution happens
   inside the tenant container.
4. Apply `SchemataTenancyOptions.TenantOverrides[id]` (the per-tenant delegates) in order.
5. Apply `SchemataTenancyOptions.DynamicOverrides` (each `Action<string, IServiceCollection,
   IServiceProvider>`) in order.
6. After each override delegate, validate the newly added descriptors: only
   `ServiceLifetime.Singleton` is accepted; a Scoped or Transient registration throws
   `InvalidOperationException` naming the offending service type.
7. Build the overrides container and wrap it in `TenantCompositeServiceProvider(overrides, root)`.

`TenantCompositeServiceProvider.GetService` checks the tenant overrides first and falls back to the
host root, with three exceptions: `IServiceScopeFactory` returns a `CompositeScopeFactory`,
`IServiceProvider` returns the composite itself, and `IEnumerable<>` lookups merge host-root and
tenant registrations so collection bindings stay coherent.

## Override registration

`ForAll` and `ForTenant` on the builder populate these registrations:

| Method                                 | Lands in                                                                     | Allowed lifetimes                                                                           |
| -------------------------------------- | ---------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| `ForAll(configure)`                    | Root `IServiceCollection` (host)                                             | Any — these are normal host services seen by every tenant via the composite's root fallback |
| `ForTenant(tenantId, configure)`       | `SchemataTenancyOptions.TenantOverrides[tenantId]`                           | Singleton only                                                                              |
| `ForTenant((id, services, root) => …)` | `SchemataTenancyOptions.DynamicOverrides`, applied to every tenant container | Singleton only                                                                              |

A tenant-aware service that must participate in the per-request scope (an `AddDbContext`, a
repository) belongs in `ForAll`; it reads the right tenant by consulting
`ITenantContextAccessor<TTenant>` at construction.

## Provider lease lifecycle

`ITenantProviderLease : IDisposable` is a refcounted handle over a cached provider, exposing a
single `Provider` property. `MemoryCacheTenantProviderCache.Lease(id, factory)` either hands back a
fresh lease over an existing entry (refreshing its LRU position) or builds a new provider via
`factory()`. The cache holds at most `SchemataTenancyOptions.ProviderMaxCapacity` entries (default
1000); entries idle longer than `SchemataTenancyOptions.ProviderSlidingExpiration` (default 30
minutes) are evicted on the next access. Eviction and explicit `Remove(id)` retire the entry, but
the underlying provider is disposed only after the last outstanding lease is released — so an entry
can be retired while a request still uses a scope built from it.

## Tenant scopes

`SchemataTenantServiceScopeFactory<TTenant>.CreateScope()`: when no tenant is bound, it returns a
host scope. Otherwise it leases a tenant provider, creates a scope over it
(`CompositeScope` layering the tenant overrides above a fresh host scope), and wraps the two in a
`LeasedTenantScope` that disposes the inner host scope first, then releases the lease.

## Tenant entities

`SchemataTenant` implements `IIdentifier`, `ICanonicalName`, `IDescriptive`, `IConcurrency`, and
`ITimestamp`, keyed by `Guid Uid`. Table `SchemataTenants`, canonical name `tenants/{tenant}`. Its
`Hosts` navigation links to `SchemataTenantHost` (table `SchemataTenantHosts`, canonical name
`tenants/{tenant}/hosts/{host}`); the host's `Name` is `[NotMapped]` and projects the normalized
`Host` string used by `RequestHostResolver`.

## TenantResolveException

`Schemata.Abstractions.Exceptions.TenantResolveException` (HTTP 400, gRPC `FAILED_PRECONDITION`) is
raised when a resolver reads a malformed Guid, when a resolved id has no matching tenant, when the
host resolver finds no tenant for the `Host` header, or when the provider factory is asked to build
a container with no bound tenant.

## Extension points

| Interface                                | Purpose                                                           |
| ---------------------------------------- | ----------------------------------------------------------------- |
| `ITenantResolver`                        | Add a resolution strategy (register directly to combine sources). |
| `ITenantManager<TTenant>`                | Replace the Repository-backed manager.                            |
| `ITenantServiceProviderFactory<TTenant>` | Replace the lease-based factory.                                  |
| `ITenantProviderCache`                   | Plug in a different cache while preserving lease semantics.       |
| `SchemataTenancyOptions`                 | Tune capacity, sliding expiration, and overrides.                 |

## Caveats

- The `Priority`/`Order` split is intentional: middleware ordering stays low while DI registration
  runs last.
- Per-tenant overrides must be Singleton. A Scoped or Transient registration throws
  `InvalidOperationException` at provider-build time.
- `ITenantContextAccessor<TTenant>` inside a tenant container is `TenantBoundContextAccessor`; it is
  fixed for the scope's lifetime.
- `IEnumerable<>` resolutions on the composite merge host and tenant registrations; a tenant
  override cannot replace a host collection wholesale.

## See also

- [Multi-Tenancy guide](../guides/multi-tenancy.md) — resolution and isolation on the Student app
- [Multi-Tenant cookbook](../cookbook/multi-tenant-cookbook.md) — per-tenant data and DI overrides
- [Built-in Features](core/built-in-features.md) — feature priority table
