# Tenancy

Schemata provides a multi-tenant architecture with pluggable tenant resolution, per-tenant DI containers, and request-scoped tenant context.

## Packages

| Package                       | Role                                       |
| ----------------------------- | ------------------------------------------ |
| `Schemata.Tenancy.Skeleton`   | Interfaces, entity type, services, manager |
| `Schemata.Tenancy.Foundation` | Feature, middleware, resolvers, builder    |

## SchemataTenant\<TKey\>

The base tenant entity, parameterized by key type (typically `Guid` or `long`):

```csharp
public class SchemataTenant<TKey> : IIdentifier, ICanonicalName, IDisplayName, IConcurrency, ITimestamp
    where TKey : struct, IEquatable<TKey>
```

Table: `SchemataTenants`. Canonical name: `tenants/{tenant}`.

Key properties:

- `TenantId` -- the tenant-specific identifier used for resolution (type `TKey?`)
- `Hosts` -- JSON-serialized list of host names for host-based resolution
- `Id` -- the `long` primary key (from `IIdentifier`)
- `DisplayName`, `DisplayNames` -- display name with localization support

## Core interfaces

### ITenantResolver\<TKey\>

Resolves the current tenant identifier from the request context:

```csharp
public interface ITenantResolver<TKey>
    where TKey : struct, IEquatable<TKey>
{
    Task<TKey?> ResolveAsync(CancellationToken ct);
}
```

Returns `null` when no tenant can be determined from the request.

### ITenantContextAccessor\<TTenant, TKey\>

Provides access to the resolved tenant within a request scope:

```csharp
public interface ITenantContextAccessor<TTenant, TKey>
{
    TTenant? Tenant { get; }
    Task InitializeAsync(CancellationToken ct);
    Task InitializeAsync(TTenant tenant, CancellationToken ct);
    Task<IServiceProvider> GetBaseServiceProviderAsync(CancellationToken ct);
}
```

The non-generic `ITenantContextAccessor` is a convenience alias for `ITenantContextAccessor<SchemataTenant<Guid>, Guid>`.

### ITenantManager\<TTenant, TKey\>

CRUD operations and lookup methods for tenant entities:

- `FindByIdAsync(long id, ct)` -- by primary key
- `FindByTenantId(TKey identifier, ct)` -- by tenant-specific identifier
- `FindByHost(string host, ct)` -- by host name (matches against JSON `Hosts` field)
- `GetHostsAsync(tenant, ct)` -- deserializes host names with in-memory caching
- `SetTenantId`, `SetDisplayNameAsync`, `SetDisplayNamesAsync`, `SetHostsAsync` -- property setters
- `CreateAsync`, `DeleteAsync`, `UpdateAsync` -- persistence operations via the repository

The non-generic `ITenantManager` is a convenience alias for `ITenantManager<SchemataTenant<Guid>, Guid>`.

### ITenantServiceProviderFactory\<TTenant, TKey\>

Creates isolated `IServiceProvider` instances scoped to a specific tenant. Each tenant gets its own DI container built from the root service collection with tenant-specific overrides. Service providers are cached per tenant.

### ITenantServiceScopeFactory\<TTenant, TKey\>

Extends `IServiceScopeFactory`. When a tenant is resolved, scopes are created from the tenant's isolated container. When no tenant is resolved, scopes fall back to the root provider.

## UseTenancy()

```csharp
builder.UseTenancy(configure: (services, tenant) => {
    // Register per-tenant service overrides here
});
```

### Overloads

- `UseTenancy()` -- uses `SchemataTenant<Guid>` with `Guid` keys and the default manager
- `UseTenancy<TTenant, TKey>()` -- uses a custom tenant type with the default `SchemataTenantManager<TTenant, TKey>`
- `UseTenancy<TManager, TTenant, TKey>()` -- uses a custom manager and tenant type

Returns a `SchemataTenancyBuilder<TTenant, TKey>` for configuring resolvers.

### Feature behavior

`SchemataTenancyFeature` registers:

- `ITenantManager<TTenant, TKey>` (scoped)
- `SchemataTenantContextAccessor<TTenant, TKey>` (scoped) and `ITenantContextAccessor<TTenant, TKey>` (transient, forwarding)
- `SchemataTenantServiceScopeFactory<TTenant, TKey>` (scoped) and `ITenantServiceScopeFactory<TTenant, TKey>` (transient, forwarding)
- `SchemataTenantServiceProviderFactory<TTenant, TKey>` (singleton)
- When using the default `SchemataTenant<Guid>`, also registers the non-generic `ITenantContextAccessor` and `ITenantManager` aliases

The `configure` delegate passed to `UseTenancy()` is invoked by `SchemataTenantServiceProviderFactory` when building per-tenant containers. This is where you register services that should differ between tenants (e.g., different database connections).

## Middleware

Two middleware components are added to the pipeline in order:

### SchemataTenantContextAccessorInitializer

Runs early in the pipeline. Calls `ITenantContextAccessor.InitializeAsync()`, which uses the registered `ITenantResolver<TKey>` to resolve the tenant identifier and look up the tenant entity via `ITenantManager`.

### SchemataTenantServiceProviderReplacer

Runs after initialization. Replaces the `IServiceProvidersFeature` on the HTTP context so that all downstream middleware and controllers resolve services from the tenant-scoped container. Restores the original provider in a `finally` block.

## Tenant resolvers

Register a resolver via the `SchemataTenancyBuilder`:

### Header resolver

```csharp
builder.UseTenancy()
       .UseHeaderResolver<SchemataTenant<Guid>, Guid>();
```

Reads the `x-tenant-id` HTTP header. Returns `null` if the header is absent. Throws `TenantResolveException` if the header is present but the value cannot be parsed. Requires `TKey : IParsable<TKey>`.

### Host resolver

```csharp
.UseHostResolver<SchemataTenant<Guid>, Guid>()
```

Matches the request `Host` header against tenant host names stored in the database. Uses `ITenantManager.FindByHost()`. Throws `TenantResolveException` if no matching tenant is found.

### Path resolver

```csharp
.UsePathResolver<SchemataTenant<Guid>, Guid>()
```

Extracts the tenant identifier from the `{Tenant}` route parameter. Requires `TKey : IParsable<TKey>`.

### Principal resolver

```csharp
.UsePrincipalResolver<SchemataTenant<Guid>, Guid>()
```

Extracts the tenant identifier from the authenticated user's `Tenant` claim. Requires `TKey : IParsable<TKey>`.

### Query resolver

```csharp
.UseQueryResolver<SchemataTenant<Guid>, Guid>()
```

Extracts the tenant identifier from the `Tenant` query string parameter. Requires `TKey : IParsable<TKey>`.

All resolver registrations use `TryAddScoped`, so only the first registered resolver for a given `TKey` is used.
