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
| `Schemata.Tenancy.Skeleton`   | `Tenancy/TenantIdentity.cs`, `Tenancy/TenantResolveException.cs` — the scoped runtime identity and resolution failure; `Schemata.Abstractions` keeps only `ITenant` and the error constants |
| `Schemata.Tenancy.Foundation` | `Services/` — `SchemataTenantContextAccessor`, `SchemataTenantManager`, `SchemataTenantServiceProviderFactory`, `SchemataTenantServiceScopeFactory`, `MemoryCacheTenantProviderCache`, `TenantCompositeServiceProvider`, `CompositeScope`, `TenantBoundContextAccessor` |
| `Schemata.Tenancy.Foundation` | `Handlers/` — `CreateTenantHandler`, `UpdateTenantHandler`, `DeleteTenantHandler`, `SetTenantDisplayNameHandler`, `SetTenantLocalizedDisplayNamesHandler`, `SetTenantHostsHandler`, `FindTenantByIdHandler`, `FindTenantByHostHandler`, `GetTenantHostsHandler` |
| `Schemata.Tenancy.Foundation` | `Features/SchemataTenancyFeature.cs`, `Features/SchemataTenantPrincipalFeature.cs`, `Middlewares/SchemataTenancyMiddleware.cs`, `SchemataTenancyBuilder.cs` |
| `Schemata.Tenancy.Foundation` | `Extensions/SchemataTenancyBuilderExtensions.cs` (resolvers), `Extensions/SchemataTenancyBuilderOverrideExtensions.cs` (overrides) |
| `Schemata.Tenancy.Foundation` | `Resolvers/Request{Header,Host,Path,Principal,Query}Resolver.cs`, `Resolvers/TenantId.cs` |
| `Schemata.Tenancy.Messaging` | `TenantMessageContextPropagator.cs`, `TenantMessageExecutionScopeFactory.cs`, `TenantMessagingExtensions.cs` |
| `Schemata.Tenancy.Caching`    | `TenantCacheBuilder.cs`, `TenantCacheKeyNormalizer.cs`, `Extensions/TenantCacheExtensions.cs` |
| `Schemata.Tenancy.Actor` | `TenantActorSystem.cs`, `ScopedTenantActorSystem.cs`, `TenantActorExtensions.cs` |

Tenant Update, invariant display-name and localized display-name handlers resolve fresh repositories
from the current operation's service provider for every public manager call. Each operation owns its
unit of work and invalidates the provider cache only after an Applied mutation commits durably.
Sequential calls through one scoped manager retain that boundary without reopening a completed
externally owned repository enlistment.


## Enabling the feature

```csharp
builder.UseSchemata(schema => {
    var tenancy = schema.UseTenancy()       // SchemataTenant + SchemataTenantHost, default manager
                     .UseHeaderResolver(); // x-tenant-id
});

schema.UseTenancy<MyTenant>();                          // custom tenant, default host entity and manager
schema.UseTenancy<MyTenant, MyTenantHost>();            // custom tenant and host entities, default manager
schema.UseTenancy<MyTenantManager, MyTenant, MyTenantHost>(); // custom manager and entities
```

Every overload returns a `SchemataTenancyBuilder<TTenant>` for chaining resolver and override
registrations. Constraints: `TTenant : SchemataTenant`, `TTenantHost : SchemataTenantHost, new()`
and, for the three-type form, `TManager : class, ITenantManager<TTenant>`. The builder registers two
features: `SchemataTenancyFeature<TManager, TTenant, TTenantHost>`
(request-stage resolution and per-request scope binding) and
`SchemataTenantPrincipalFeature<TTenant>` (Principal-stage resolver invocation after authentication).

`SchemataTenancyFeature<TManager, TTenant, TTenantHost>` has `Priority = SchemataCorsFeature.DefaultPriority +
5_000_000 = 205_000_000` and `Order = Orders.Max = 900_000_000`. `ConfigureServices` registers scoped
host `TenantIdentity`, the nine closed handlers (one per `ITenantManager<TTenant>` operation), and
decorates `IPolicyEvaluator` with `TenantPolicyEvaluator`. The exact responsibilities:

| Block                                                                                         | Source                                                                                              |
| --------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| `AddOptions<SchemataTenancyOptions>()` and scoped host `TenantIdentity`                    | `SchemataTenancyFeature.ConfigureServices`                                                          |
| `IDecorator<IPolicyEvaluator>` for tenant-aware authorization                              | `SchemataTenancyFeature.ConfigureServices`                                                        |
| Quota rejection postconfiguration (`TenantQuotaOptions`)                                       | `SchemataTenancyFeature.ConfigureServices`                                                          |
| Nine `TryAddScoped<…Handler<TTenant, TTenantHost>>()` registrations                            | `SchemataTenancyFeature.ConfigureServices`                                                          |
| `TryAddScoped<ITenantManager<TTenant>, TManager>()` and accessor + interface alias resolution | `SchemataTenancyFeature.ConfigureServices`                                                          |
| `ITenantServiceScopeFactory<TTenant>`, `ITenantProviderCache`, `ITenantServiceProviderFactory<TTenant>` singletons | `SchemataTenancyFeature.ConfigureServices`                                                   |

`SchemataTenantContextAccessor<TTenant>` implements both `ITenantContextAccessor<TTenant>` (read)
and `ITenantContextInitializer<TTenant>` (write); the two interfaces resolve to the same scoped
instance. The accessor takes an `IEnumerable<ITenantResolver>` — multiple resolvers participate
together, not a single one. `ConfigureApplication` adds `SchemataTenancyMiddleware<TTenant>` at priority 205_000_000
(`SchemataCorsFeature.DefaultPriority + 5_000_000`) and `SchemataTenantPrincipalMiddleware<TTenant>`
at 211_000_000 (`SchemataAuthenticationFeature.DefaultPriority + 1_000_000`).

## Repository isolation

Register each tenant-data entity through `SchemataTenancyBuilder<TTenant>.UseRepositories(...)`,
which composes per-tenant registrations through the dynamic tenant overrides:

```csharp
schema.UseTenancy<MyTenant>()
      .UseRepositories(tenant => {
          tenant.Services.AddTransient<IRepository<Invoice>, EfCoreRepository<AppDbContext, Invoice>>();
          tenant.Services.AddScoped<IResourceMutation<Invoice>, ResourceMutation<Invoice>>();
          tenant.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));
      });
```

`UseRepositories` installs enumerable add, build-query, update, and remove advisors inside every
tenant service collection and binds the verified identifier as a `TenantRepositoryBinding`. The
advisors apply to entities implementing `ITenant` and return `AdviceResult.Continue` for every
other entity, so a tenant that registers only non-tenanted repositories keeps plain per-tenant
resolution. Register the repository implementation, its native source, and the closed
`IResourceMutation<TEntity>` in `tenant.Services`; a tenant without the mutation registration
resolves the host open-generic mutation, which binds the host repository and refuses to join a
tenant unit of work.

Shared-table isolation requires `ITenant`, `IIdentifier`, and `IConcurrency`, an explicit Schemata
`[PrimaryKey(nameof(Uid))]` declaring the sole `Guid Uid` key, and a concrete `[ConcurrencyCheck]`
timestamp. All three properties require mapped public instance getters and existing setters;
`[NotMapped]` declarations are rejected. Repository key fallback alone does not declare a mapped
provider key. UIDs identify stable entity instances; consumers must not reuse them for
replacements. Tenant catalog types (`SchemataTenant` and `SchemataTenantHost`, including
subclasses) remain host metadata: they do not implement `ITenant`, the isolation advisors leave
them untouched, and `UseDatabase` rejects them at registration.

The advisor identity is a fixed `TenantRepositoryBinding` constructed from the verified tenant
identifier when the tenant container is composed. `Repository/AdviceAddTenant.cs` stamps that
identity before canonical-name and uniqueness advice. `Repository/AdviceBuildQueryTenant.cs`
captures it into a concrete `TenantUid` equality predicate. A tenant-bound repository retains that
identity independently of other scopes. Host-registered repositories resolve through the host
binding and see `TenantUid IS NULL` rows only;
declaring the same entity in both a global registration and a tenant registration keeps the two
scopes separate. Owner-query suppression keeps the tenant predicate installed. Background callers
create their selected execution scope and use its scoped `TenantIdentity` and services through
repository operations and disposal. Tenant-specific repository constructor dependencies require
explicit tenant registrations; advisors do not clone the host service graph.

Tenant isolation through the repository covers `IRepository<TEntity>` operations. Direct
`DbContext`/`DataConnection` access, raw SQL, and ORM bulk operations bypass the advisor chain and
are the application's responsibility.

## Database per tenant

`TenantDatabaseExtensions.UseDatabase<TTenant, TEntity, TRepository>(configureSource)` registers a
repository whose database target is resolved per tenant container. The callback registers the
native provider factory at tenant scope — `AddRepositorySource<TContext>` for EF Core or
LinqToDB — with a tenant-scoped lifetime, so the composing tenant's configuration is
authoritative:

```csharp
tenancy.UseRepositories(tenant => tenant
    .UseDatabase<MyTenant, Invoice, EfCoreRepository<AppDbContext, Invoice>>(services =>
        services.AddRepositorySource<AppDbContext>(
            (sp, options) => options.UseSqlite(ConnectionStringFor(tenant.TenantUid)),
            ServiceLifetime.Scoped)));
```

`UseDatabase` entities do not require the `ITenant` trait; physical separation replaces the
shared-table predicate. A missing tenant factory registration fails at composition rather than
falling back to the host database. Repositories and units of work capture a binding at
construction, and joining a unit of work across different sources or tenant bindings throws
`InvalidOperationException` instead of sharing a transaction. Database schema creation and
migration for tenant databases are the application's responsibility.

`Repository/TenantMutation.cs` calls `IRepository<TEntity>.GetOriginalAsync` for the original tenant
scalar and compares it with caller and staged identities. The repository selects by its primary-key
metadata through the ordinary build-query advisors; its `OriginalResult<T>.Found` distinguishes a
missing row from a found host row with a null tenant. A missing row or identity mismatch throws
`PermissionDeniedException`. When the owned original exists, a denied reparent restores its tenant
identity on the staged entity. A caller-owned unit of work must roll back or dispose after a denial
whose original row is unavailable; `IUnitOfWork.RollbackAsync()` completes that unit of work.
The lookup passes `QueryOptions(IgnoreSoftDelete: true)` for undelete and physical-delete operations while
retaining tenant and ownership query advisors. Update validation runs after the standard validation
and reference advisors; removal validates before soft-delete handling, whose nested update validates
again. The providers' timestamp compare-and-swap guards writes between validation and persistence.
Consumer advisors are trusted extensions and must preserve tenant identity after validation.

The EF provider captures `PropertyValues.Clone()` after successful add advisors and staging. It
materializes a private query object once, on the first staged original lookup.
`EfCoreUnitOfWork<TContext>` owns these facts across transient repositories
joined to its context. An original query uses that baseline only for the same admitted entity reference,
an unchanged actual provider primary key, and a still-Added tracker entry; normal build-query advisors
filter the baseline before projection. The public selector accepts a direct immutable scalar property,
so callers cannot retrieve and mutate the cached query object. Commit, failed commit, rollback, and
disposal clear admission facts. LinqToDB uses the persisted query path because inserts execute within
its transaction. Persisted scalar projections leave EF entity tracking untouched.


Mark concrete `TenantUid` properties with the serializers' ordinary ignore metadata and keep the
scalar mapped to storage. Consumer business fields and canonical references retain their usual shape.

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
initializer) and calls `InitializeAsync(TenantResolutionStage.Request, ct)`. It opens a tenant
scope through `ITenantServiceScopeFactory<TTenant>` via `TenantRequestBinding.OpenAsync`, sets
`http.RequestServices` to the tenant scope's `ServiceProvider`, stores the binding in
`http.Features`, and restores the original in a `finally`. `SchemataTenantPrincipalMiddleware`
calls `binding.BindPrincipalAsync(http)` after authentication, which calls
`InitializeAsync(TenantResolutionStage.Principal, ct)` and reopens the scope only if the
resulting `TenantIdentity` differs from the binding's current `Identity`. Controllers and handlers
resolve the fixed scoped `TenantIdentity` from the active `RequestServices`, including during
disposal; reverse-order scope disposal is asynchronous.

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
an `ITenantProviderLease`. `MemoryCacheTenantProviderCache.LeaseVersionAsync(id, version, create, ct)`
keys each entry by `{id.Length}:{id}:{version:N}` where `version` is the tenant's `Timestamp` (Guid)
— a change in `Timestamp` forces a fresh build. Concurrent misses for one key share a single build;
callers waiting on it complete asynchronously and observe `ct`. Building a container:

1. Start from an empty `ServiceCollection`.
2. Register the resolved `TTenant` instance as a Singleton.
3. Register scoped `TenantIdentity` from the verified tenant UID before tenant overrides.
4. Register `TenantBoundContextAccessor<TTenant>` as the `ITenantContextAccessor<TTenant>` Singleton
   for the tenant scope — it pins the tenant at construction, so no per-request resolution happens
   inside the tenant container.
5. Apply `SchemataTenancyOptions.TenantOverrides[id]` (the per-tenant delegates) in order.
6. Apply `SchemataTenancyOptions.DynamicOverrides` (each `Action<string, IServiceCollection,
   IServiceProvider>`) in order.
7. Validate each added descriptor: open-generic service types or open-generic implementation types
   raise `InvalidOperationException` naming the offending service type. `Singleton`, `Scoped`, and
   `Transient` lifetimes are accepted; the composite provider resolves scoped services from the
   underlying tenant scope and transient services per-call.
8. Build the overrides container and wrap it in `TenantCompositeServiceProvider(overrides, root)`.

`TenantCompositeServiceProvider.GetService` checks the tenant overrides first and falls back to the
host root. Resolving `IServiceProvider`, `IServiceScopeFactory`, `IKeyedServiceProvider`,
`IServiceProviderIsService`, or `IServiceProviderIsKeyedService` returns the composite itself, and
`IEnumerable<>` lookups merge host-root and tenant registrations so collection bindings stay
coherent. Scoped resolutions route through `TenantResolutionContext.Services` so the lifetime
matches the per-request scope that owns the tenant binding.

`TenantCompositeServiceProvider` is its own `IServiceScopeFactory`. `CreateScope` opens a fresh
host scope, leases the cached provider entry again through the `LeaseSource` delegate the provider
cache attaches once the entry is committed, and returns a `CompositeScope` that pairs the tenant
overrides scope with that host scope and owns the lease. Each scope built from the provider leases
the same committed entry, so a scope stays usable after a parent scope or the cache entry is
retired; disposal releases the lease exactly once, after both inner scopes. A retired entry is
never rebuilt under the same key.

## Override registration

`ForAll` and `ForTenant` on the builder populate these registrations:

| Method                                 | Lands in                                                                     | Allowed lifetimes                                                                            |
| -------------------------------------- | ---------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| `ForAll(configure)`                    | Root `IServiceCollection` (host)                                             | Any — these are normal host services seen by every tenant via the composite's root fallback |
| `ForTenant(tenantId, configure)`       | `SchemataTenancyOptions.TenantOverrides[tenantId]`                           | Singleton, Scoped, or Transient; open-generic descriptors are rejected at provider build     |
| `ForTenant((id, services, root) => …)` | `SchemataTenancyOptions.DynamicOverrides`, applied to every tenant container | Singleton, Scoped, or Transient; open-generic descriptors are rejected at provider build     |

Host registrations remain constructed by host DI, including their transitive dependencies.
A host singleton that needs execution identity accepts the final invocation `IServiceProvider` and
resolves the scoped `TenantIdentity` on each operation. Controllers, handlers, and their scoped
collaborators can inject that identity directly, including during disposal. Register services
requiring tenant-specific constructor dependencies, including repositories and their collaborators,
explicitly through `ForTenant(...)`. A host-registered service's scoped
`ITenantContextAccessor<TTenant>` comes from host DI; it is not the tenant container's
`TenantBoundContextAccessor` and must not supply the current tenant identity for repository advice.

## Provider lease lifecycle

`ITenantProviderLease : IDisposable, IAsyncDisposable` is a refcounted handle over a cached
provider, exposing a single `Provider` property. `LeaseVersionAsync(id, version, create, ct)`
either hands back a fresh lease over an existing entry (refreshing its LRU position) or builds a
new provider via `create()`. The cache holds at most `SchemataTenancyOptions.ProviderMaxCapacity`
entries (default 1000); entries idle longer than `SchemataTenancyOptions.ProviderSlidingExpiration`
(default 30 minutes) are evicted on the next access. Eviction and explicit `RemoveAsync(id)` retire
the entry, but the underlying provider is disposed only after the last outstanding lease is
released. `DisposeAsync()` awaits an `IAsyncDisposable` provider once and otherwise calls
`IDisposable.Dispose()` once. Composite tenant providers expose asynchronous teardown for arbitrary
consumer registrations. Synchronous release rejects before disposing their container; the lease
and cache retain the cleanup obligation for a later `DisposeAsync()`. Genuinely synchronous
providers retain synchronous teardown. A rejected synchronous release never blocks on async work.
Leased scopes attempt inner-scope cleanup and provider-lease release independently. When both fail,
the inner cleanup failure is first in `AggregateException`; a later `DisposeAsync()` discharges
the pending lease obligation without repeating inner-scope cleanup.
An entry can be retired while a request still uses a scope built from it. A lease acquired
while the entry was current but later retired keeps using its captured provider; subsequent leases
on the same id trigger a rebuild keyed by the tenant's current `Timestamp`. Host shutdown also
retires providers and defers disposal for outstanding leases.

Cleanup attempts every eligible provider before propagating failures: a single error retains its
original exception, and multiple errors produce an `AggregateException`. If expiration or capacity
cleanup fails during acquisition, the cache releases that acquisition's undelivered lease before
throwing; existing handles remain valid. A provider disposal attempt consumes its one cleanup
attempt even when it throws. Synchronous rejection before provider entry retains the asynchronous
cleanup obligation. `ITenantProviderCache` owns container
leases; `ICacheProvider` owns cached values, with tenant key framing supplied by the tenant
outlet's `TenantCacheKeyNormalizer`. See
`src/Schemata.Tenancy.Foundation/Services/MemoryCacheTenantProviderCache.cs`
for acquisition compensation, retirement, and synchronous/asynchronous cleanup.

## Tenant scopes

`SchemataTenantServiceScopeFactory<TTenant>.CreateAsync(identity, ct)`: when `identity` is
`TenantIdentity.Host`, it returns a host scope. Otherwise it leases a tenant provider, creates a
scope over it (`CompositeScope` layering the tenant overrides above a fresh host scope), and wraps
the two in a `LeasedTenantScope` that disposes the inner host scope first, then releases the lease.
Creating the scope establishes its DI lifetime and fixes the scoped `TenantIdentity` resolved from
`scope.ServiceProvider`. A direct caller uses that provider for service use and disposal; parallel
scopes retain independent identities across awaits. Tenant-bound services (repositories, cache
outlets, state profiles) capture the binding at composition, not per call; the installed
`TenantMessageContextPropagator` exports the provider's identity when `MessageContexts.Capture`
iterates the propagator collection. `MessageContexts` itself only carries generic items.

## Tenant entities

`SchemataTenant` implements `IIdentifier`, `ICanonicalName`, `IDescriptive`, `IConcurrency`, and
`ITimestamp`, keyed by `Guid Uid`. Table `SchemataTenants`, canonical name `tenants/{tenant}`. Its
`Hosts` navigation links to `SchemataTenantHost` (table `SchemataTenantHosts`, canonical name
`tenants/{tenant}/hosts/{host}`); the host's `Name` is `[NotMapped]` and projects the normalized
`Host` string used by `RequestHostResolver`.

When `UseTenancy<TTenant, TTenantHost>()` selects subclasses, EF Core would otherwise keep the
unselected CLR base types in the mapped hierarchy, because both base types are concrete and
`SchemataTenant.Hosts` targets `SchemataTenantHost`. Map the selected types as roots and drop the
base-typed navigation:

```csharp
protected override void OnModelCreating(ModelBuilder model)
{
    model.Entity<Catalog>(builder => {
        builder.HasBaseType((Type?)null);
        builder.Ignore(row => row.Hosts);
    });
    model.Entity<CatalogHost>().HasBaseType((Type?)null);
}
```

## Messaging bridge

`Schemata.Tenancy.Messaging` installs tenant-owned propagation and execution-scope services for
messages and request dispatches crossing DI scopes, threads, or processes. Explicit
`.UseMessaging()` activates both. Actor runtime isolation uses `.UseActor()` from
`Schemata.Tenancy.Actor`, which also installs the messaging bridge.

```csharp
schema.UseTenancy<MyTenant>()
      .UseHeaderResolver()
      .UseMessaging();                       // Schemata.Tenancy.Messaging
```

`TenantMessagingExtensions.UseMessaging<TTenant>` replaces `IMessageExecutionScopeFactory` with
`TenantMessageExecutionScopeFactory<TTenant>`, registers `IMessageExecutionScopeRunner`, and adds
`TenantMessageContextPropagator` to the enumerable propagator collection. The propagator captures
the source provider's scoped `TenantIdentity` into the `tenancy.tenant-id` item as a GUID or `host`.
The tenant factory:

1. Parses that item through `TenantMessageContextPropagator.Identity(context?.Items)`;
   an absent item selects host and a malformed or empty GUID raises `TenantResolveException`.
2. Opens a bootstrap host scope, resolves `ITenantServiceScopeFactory<TTenant>`, creates the
   selected DI scope, and disposes the bootstrap before returning.
3. Returns a `MessageExecutionScope` exposing `Services`, `RestoreAsync`, and `DisposeAsync`.
   Restoration validates the captured identity against the scoped identity in the final provider.

`IMessageExecutionScopeRunner.RunAsync<T>` performs creation, restoration, the work delegate, and
asynchronous disposal in that order and returns the work result. Creation failure skips work and
has no scope to dispose. Restore or work failure disposes the created scope. One failure propagates
unchanged; simultaneous body and cleanup failures produce `AggregateException(body, cleanup)`.
Scheduling, event, RabbitMQ, reminder, and tenant actor send/stop operations use this runner. Actor
callbacks, report recovery, plain dispatch, and stream enumeration retain their specialized
restoration and cleanup policies through the same scope primitives.

The neutral `MessageExecutionScopeFactory` accepts any `MessageContext`, creates a host DI scope,
and leaves propagation to the registered propagators. Without the tenancy bridge, those neutral
services perform no tenant parsing, tenant rejection, or tenant scope selection.
Sources: `src/Schemata.Messaging.Skeleton/Runtime/MessageExecutionScopeRunner.cs`,
`MessageExecutionScopeFactory.cs`, `MessageContexts.cs`, `MessageExecutionScope.cs`, and
`src/Schemata.Tenancy.Messaging/`.

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

## Actor bridge

Enable tenant actor isolation through `schema.UseTenancy<MyTenant>().UseActor()` after registering
the ordinary Actor feature. `TenantActorExtensions.UseActor<TTenant>` installs the messaging bridge,
registers singleton `TenantActorSystem` as `IActorSystemLifetime`, and binds scoped `IActorSystem`
to `ScopedTenantActorSystem`. The owner keeps one `InProcessActorSystem` per host or tenant identity;
`ActorId` keeps only its type and business key. The same ID can address independent activations in
different tenants.

Actor references capture the generic `MessageContext` at creation. Every tenant Tell/Ask requires
current explicit message context with the reference's bound tenant identity before runtime or scope
restoration. Missing or foreign evidence rejects before dispatch. The original caller provider still
owns self-Ask admission. A retained reference reactivates on its bound runtime after idle collection.
Parallel operations retain independent scoped identities. Stop uses the same runner;
the owner delegates shutdown and idle collection across its runtimes. Actor callbacks use the
activation context directly through create/restore/work/dispose rather than the standard runner.
Normal actor disposal restores the activation's tenant identity in its turn scope. An
already-disposed root scope factory causes direct owned-actor disposal, async-first, exactly once;
actor cleanup failures remain observable.
Sources: `src/Schemata.Tenancy.Actor/TenantActorExtensions.cs`, `TenantActorSystem.cs`, and
`ScopedTenantActorSystem.cs`.


## Caching bridge

`Schemata.Tenancy.Caching` activates host and tenant cache partitions through
`SchemataTenancyBuilder<TTenant>.UseCache()`:

```csharp
services.AddMemoryCacheProvider();

schema.UseTenancy<MyTenant>()
      .UseCache();
```

Activation replaces the host's unkeyed `ICacheProvider` with a `CacheProvider` over the keyed
`CacheServiceKeys.Backend` and the host normalizer. Host keys use `v1\x1ehost\x1e{key}`; tenant keys
use `v1\x1etenant\x1e{uid:N}\x1e{key}`. Every tenant outlet receives its fixed normalizer at
composition, so host and tenant values remain independent on the shared backend. Resolving
`ICacheBackend` through `CacheServiceKeys.Backend` is the explicit unframed global access path.

`UseCache(configure)` performs the same activation before applying an explicit outlet declaration:

```csharp
schema.UseTenancy<MyTenant>()
      .UseCache(tenant => tenant.UseShared(sharedBackend));
// or
      .UseCache(tenant => tenant.UseDedicated(sp => new MemoryCacheBackend()));
```

A callback with no outlet declaration installs the shared keyed-backend fallback with one key
normalization. `UseShared` borrows an application-owned backend; the application retains disposal.
`UseDedicated` creates the backend through the factory once per tenant container, and the tenant
provider disposes it after the retired provider's last lease is released. The two explicit modes
are mutually exclusive per outlet: repeating the identical declaration is a no-op and a conflicting
declaration fails at tenant composition. Prefixing a shared Redis database is the shared mode, not
a dedicated instance.

Every mode constructs a fixed `CacheProvider` over the bound backend and normalizer. Cache value
shape, TTL, and atomic conditional operations remain the backend's contract; the outlet adds exactly
one synchronous key normalization per operation.
Sources: `src/Schemata.Tenancy.Caching/TenantCacheBuilder.cs`, `TenantCacheKeyNormalizer.cs`,
`Extensions/TenantCacheExtensions.cs`, and `src/Schemata.Caching.Skeleton/CacheProvider.cs`.

## TenantResolveException

`Schemata.Tenancy.Skeleton.Tenancy.TenantResolveException` (HTTP 400, gRPC `FAILED_PRECONDITION`) is
raised when a resolver reads a malformed GUID, a resolved ID has no matching tenant, the host
resolver finds no tenant for the request host, the installed message propagator parses malformed
tenant data or restores a different identity, or the tenant provider factory receives no bound tenant.

## Extension points

| Interface                                | Purpose                                                           |
| ---------------------------------------- | ----------------------------------------------------------------- |
| `ITenantResolver`                        | Add a resolution strategy; multiple resolvers compose per stage.  |
| `ITenantManager<TTenant>`                | Replace the dispatcher-backed manager.                            |
| `ITenantServiceProviderFactory<TTenant>` | Replace the lease-based factory.                                  |
| `ITenantProviderCache`                   | Plug in a different cache while preserving lease semantics.       |
| `IMessageExecutionScopeFactory`          | Replace the messaging execution scope factory (see messaging bridge). |
| `SchemataTenancyOptions`                 | Tune capacity, sliding expiration, and overrides.                 |

## Cache, data protection, and state boundaries

Cache operations inside a tenant scope resolve the tenant outlet activated by `UseCache`, which
binds the verified tenant identity at composition. Conditional writes and collection operations
retain the backend's atomicity; the outlet adds one key normalization. Host-global consumers use
the raw keyed backend. Enabling tenancy alone leaves the host `ICacheProvider` registration and
unframed composite fallback unchanged; `UseCache` is the explicit activation boundary for host and
tenant framing.

`UseSessionCache` registers the tenant `IDistributedCache` over that outlet. Asynchronous members
(`GetAsync`, `SetAsync`, `RefreshAsync`, `RemoveAsync`) are the request path and return the cache
task directly. Synchronous members use the tenant `ISynchronousCacheBackend` binding, which
applies the tenant frame and rejects each operation with `NotSupportedException` when the outlet
is selected over an asynchronous backend such as Redis.

ASP.NET Core's `DistributedSession` loads lazily through the synchronous `IDistributedCache.Get`
when a request reads or writes session state before `LoadAsync` completes. With an asynchronous
backend that rejection is handled inside `DistributedSession`: the session becomes unavailable,
reads return no values, writes are discarded, and `CommitAsync` stores nothing. A session backed
by an asynchronous cache requires `await ISession.LoadAsync()` before any synchronous `ISession`
member; session state then commits through the asynchronous members during middleware shutdown.

Data protection for per-tenant state uses a fixed native derivation: `UseState(...)` registers the
host `IDataProtectionProvider`'s `CreateProtector("Schemata.Tenancy").CreateProtector(uid:N)`
protector as the tenant collection's `IDataProtectionProvider`. The host's provider registration
and every global consumer are untouched; there is no ambient per-call wrapping. The derivation
shares the host key ring — purpose separation, not separate key management; independent key rings
remain a host configuration concern.

`TenantStateBuilder` carries the opt-in state capabilities: `UseCookie(scheme, configure)`,
`UseBearer(scheme, configure)`, `UseSession<TStore>(configure)`, and
`UseAntiforgery(configure)`. Each builds the native ASP.NET Core component against the tenant
profile: closed per-tenant options (cookie name = `CookiePrefix` + logical scheme), the fixed
protector, and tenant-owned stores. Tenancy itself registers no authentication scheme, session, or
antiforgery service for a tenant: the tenant container exposes only the capabilities declared
through `UseState`, and the host keeps its own explicit registrations. Cookie tickets are
scope-bound at issuance and authentication; Session cookie, server-side store, and sliding refresh
share the profile; antiforgery cookie/request token pairs validate only within the issuing profile;
continuations keep their caller/query/snapshot bindings and reject cross-profile unprotection. A
host ticket or token is never accepted as a tenant one and vice versa.

Core's `UseSession` and `UseAntiforgery` register the native services and middleware. When Tenancy is
present, `SchemataTenancyFeature` replaces the registered feature instances at `Orders.Max` with
`TenantSessionFeature<T>` and `TenantAntiforgeryFeature`. The wrappers preserve service configuration,
`Order`, and `Priority`, and select `UseTenantStatePipeline` during application configuration. Selection
resolves `ITenantStatePipelineProvider` from the final request services, so the host and each tenant use
their own fixed provider and every request runs one native branch. `DefaultTenantStatePipelineProvider`
caches branches per `(pipeline, next)` and constructs each native `ApplicationBuilder` over that fixed
provider. Tenant session and antiforgery registrations contribute the provider; the antiforgery validator
also requires the host antiforgery capability. Core applications without Tenancy execute the native
middleware directly and have no tenant state provider resolution.

Applications using principal-only tenant resolution authenticate the global scheme first; the
principal-stage rebind then selects tenant state for operations that declare it. Request-resolved
tenants authenticate tenant-scheme tickets directly. Sources:
`src/Schemata.Tenancy.Foundation/State/` (TenantStateBuilder, TenantCookieRegistration,
TenantBearerRegistration, TenantSessionRegistration, TenantAntiforgeryRegistration,
ITenantStatePipelineProvider, TenantStatePipelineExtensions).

## Quota diagnostics

`Services/TenantQuotaOptions.cs` postconfigures the existing ASP.NET Core `RateLimiterOptions.OnRejected`
delegate through the tenancy feature. It snapshots the current identity, invokes the existing delegate,
and rethrows the same `QuotaExceededException` after prefixing fallback violation subjects with
`tenants/{uid:D}/`. A nonblank rejected-lease `QuotaMetadata.Subject` keeps application-owned subjects
unchanged. Host subjects, nonquota exceptions, retry details, and started application responses retain
the existing handler's behavior. Core selects authenticated caller claims or client IP; the Tenancy
decorator labels diagnostics only. Rate-limit partition selection remains the application policy's
responsibility. Installed-feature tests live in `tests/Schemata.Tenancy.Integration.Tests/TenantQuotaShould.cs`.

## Caveats

- The `Priority`/`Order` split is intentional: middleware ordering stays low while DI registration
  runs last.
- Tenant override descriptors must not be open generics; a `ServiceType` or implementation type
  with unclosed generic parameters raises `InvalidOperationException` at provider-build time.
- `ITenantContextAccessor<TTenant>` inside a tenant container is `TenantBoundContextAccessor`; it is
  fixed for the scope's lifetime.
- `IEnumerable<>` resolutions on the composite concatenate host-first, then tenant items; a tenant
  registration of the same concrete type replaces the host's items of that type, while the
  options families (`IConfigureOptions<>`/`IPostConfigureOptions<>`/`IValidateOptions<>`) always
  compose.
- The messaging, actor, and caching tenancy bridges are opt-in. `.UseMessaging()` and `.UseActor()`
  extend the builder returned by `UseTenancy<TTenant>()`; the message execution factory they install
  resolves that feature's `ITenantServiceScopeFactory<TTenant>`. Cache and state capabilities compose
  through `UseCache` and `UseState`; the shared cache fallback resolves the host's keyed backend,
  while an explicit dedicated outlet uses its tenant-owned registration.
- Entity Framework Core options composition differs by target framework: on EF 8 the tenant
  `DbContextOptions` configuration replaces the host's (tenant-first descriptor resolution), while
  EF 9+ aggregates host-first with tenant overrides winning. Tenant `AddRepositorySource`
  callbacks must configure a complete standalone options set, including `SchemataModelCustomizer`.

## See also

- [Multi-Tenancy guide](../guides/multi-tenancy.md) — resolution and isolation on the Student app
- [Multi-Tenant cookbook](../cookbook/multi-tenant-cookbook.md) — per-tenant data and DI overrides
- [Built-in Features](core/built-in-features.md) — feature priority table