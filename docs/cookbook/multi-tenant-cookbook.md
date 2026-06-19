# Multi-Tenant Setup

## What you'll build

A multi-tenant API where each request is resolved to a tenant using a
combination of resolvers: an `x-tenant-id` header for machine clients, a
`{Tenant}` route segment for browser-friendly URLs, and a `Tenant` claim for
authenticated users. You'll also configure per-tenant EF Core connection
strings so each tenant's data lives in its own database.

## Prerequisites

- Completed [Getting Started](../guides/getting-started.md).
- `Schemata.Tenancy.Foundation` package added.

```shell
dotnet add package --prerelease Schemata.Tenancy.Foundation
```

## Step 1 — Register the tenancy feature

Call `UseTenancy()` on the `SchemataBuilder` and pick one resolver:

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseJsonSerializer();

        schema.UseTenancy()
              .UseHeaderResolver();

        schema.ConfigureServices(services => {
            services.AddRepository(typeof(EfCoreRepository<,>))
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));
        });

        schema.UseResource()
              .MapHttp()
              .Use<Student>();
    });
```

`UseTenancy()` installs `SchemataTenancyFeature` at priority `160_000_000`
(between `Https` at `150M` and `CookiePolicy` at `170M`). Its `Order` is
`Orders.Max` (900_000_000) so DI registration runs last, after all other
features have had a chance to register their services.

The five available resolvers and their lookup sources are:

| Method | Resolver | Source |
| --- | --- | --- |
| `UseHeaderResolver()` | `RequestHeaderResolver` | `x-tenant-id` request header |
| `UseHostResolver()` | `RequestHostResolver<TTenant>` | `Host` header matched against tenant host names |
| `UsePathResolver()` | `RequestPathResolver` | `{Tenant}` route parameter |
| `UsePrincipalResolver()` | `RequestPrincipalResolver` | `Tenant` claim on the authenticated principal |
| `UseQueryResolver()` | `RequestQueryResolver` | `Tenant` query string parameter |

Each `UseXxxResolver()` extension calls `services.TryAddScoped<ITenantResolver, X>()`. Only the **first** registration sticks; subsequent calls return without modifying DI. `SchemataTenantContextAccessor<TTenant>` takes a single `ITenantResolver` from DI and asks it once per request. To combine several signals (for example, "header overrides path"), implement a composite `ITenantResolver` and register it before any `UseXxxResolver()` extension:

```csharp
public sealed class HeaderOrPathResolver(
    IHttpContextAccessor http) : ITenantResolver
{
    public Task<Guid?> ResolveAsync(CancellationToken ct = default)
    {
        var headers = http.HttpContext?.Request.Headers;
        if (headers is not null
         && headers.TryGetValue("x-tenant-id", out var raw)
         && Guid.TryParse(raw, out var id)) {
            return Task.FromResult<Guid?>(id);
        }

        if (http.HttpContext?.GetRouteValue("Tenant") is string slug
         && Guid.TryParse(slug, out var fromPath)) {
            return Task.FromResult<Guid?>(fromPath);
        }

        return Task.FromResult<Guid?>(null);
    }
}

schema.ConfigureServices(services =>
    services.AddScoped<ITenantResolver, HeaderOrPathResolver>());
schema.UseTenancy();   // No UseXxxResolver — the composite is already in DI.
```

**Verify:** Start the app and send a request with `x-tenant-id: <guid>`. The middleware resolves the tenant and makes it available via `ITenantContextAccessor<SchemataTenant>`.

## Step 2 — Use a custom tenant entity

The default `SchemataTenant` entity has `Uid`, `Name`, and `CanonicalName`.
Add a `ConnectionString` property for per-tenant database routing:

```csharp
using Schemata.Tenancy.Skeleton.Entities;

public class AppTenant : SchemataTenant
{
    public string? ConnectionString { get; set; }
}
```

Pass the custom type to `UseTenancy<TTenant>()` with one resolver — or, to
combine sources, the composite from Step 1:

```csharp
schema.UseTenancy<AppTenant>()
      .UseHeaderResolver();
```

`UseTenancy<TTenant>()` uses `SchemataTenantManager<TTenant>` as the default
manager. To supply a custom manager, use the three-argument overload:
`UseTenancy<TManager, TTenant>()`. Only one `ITenantResolver` is active per
host; stacking `UseHeaderResolver().UsePathResolver()` does not chain them —
the first wins.

**Verify:** `ITenantContextAccessor<AppTenant>.Tenant` resolves to an
`AppTenant` instance with the `ConnectionString` property populated.

## Step 3 — Configure per-tenant DI overrides

The tenancy system builds one `IServiceProvider` per tenant and caches it.
Tenant-specific singletons are registered via `SchemataTenancyOptions`. Use
`DynamicOverrides` to apply a connection string from the resolved tenant:

```csharp
schema.ConfigureServices(services => {
    services.Configure<SchemataTenancyOptions>(o => {
        o.DynamicOverrides.Add((tenantId, overrides, root) => {
            // Resolve the tenant entity from the root provider to get its
            // connection string. The tenant is already in the per-tenant
            // container as a singleton at this point.
            var tenant = overrides.BuildServiceProvider()
                                  .GetService<AppTenant>();
            if (tenant?.ConnectionString is { } cs)
            {
                overrides.AddSingleton<IDbContextFactory<AppDbContext>>(
                    _ => new TenantDbContextFactory(cs));
            }
        });
    });
});
```

`TenantCompositeServiceProvider` resolves services from the tenant-specific
container first, then falls back to the host root. `IServiceScopeFactory` is
intercepted so scoped services created inside a request still come from the
host's normal request-scope lifecycle.

**Important:** Tenant overrides must be registered as `Singleton`. The factory
enforces this at build time and throws `InvalidOperationException` if a
`Scoped` or `Transient` service is added to the overrides collection.

**Verify:** Two requests with different `x-tenant-id` headers hit different
databases. Confirm by inserting a row via one tenant and verifying it does not
appear when querying via the other.

## Step 4 — Access the current tenant in application code

Inject `ITenantContextAccessor<AppTenant>` wherever you need the current
tenant:

```csharp
public class StudentService(ITenantContextAccessor<AppTenant> accessor)
{
    public AppTenant? CurrentTenant => accessor.Tenant;
}
```

Inside a per-tenant service provider scope, `TenantBoundContextAccessor<TTenant>`
is used instead of the HTTP-based accessor. It returns the tenant that was
bound at scope creation time, so HTTP resolution is skipped.

**Verify:** Log `accessor.Tenant?.Name` in a controller action. The value
matches the tenant ID sent in the request header.

## Step 5 — Path-based routing with `{Tenant}`

`RequestPathResolver` reads the `{Tenant}` route parameter. Add it to your
route template:

```csharp
[ApiController]
[Route("{Tenant}/[controller]")]
public class StudentsController : ControllerBase { ... }
```

A request to `/acme/students` sets the tenant from the `{Tenant}` segment, as
long as `UsePathResolver()` is the active resolver. To honor both a header and a
path segment, use the composite resolver from Step 1 — registering both
extensions does not combine them.

**Verify:** `GET /acme/students` and `GET /beta/students` return data from
different tenant databases.

## Common pitfalls

**Only one resolver is ever active.** `UseXxxResolver()` calls `TryAddScoped<ITenantResolver, X>()`; the first wins and every subsequent `UseXxxResolver()` is a no-op. The middleware does not iterate resolvers. To layer multiple sources (header → path → claim), register a composite `ITenantResolver` directly and skip the `UseXxxResolver()` extensions.

**`TenantResolveException` per request** — the accessor throws it when a
resolver yields a tenant id that `ITenantManager.FindByTenantId` cannot find,
when a resolver parses a malformed Guid, and when the provider factory is asked
to build a container with no bound tenant. To run a tenant-scoped service
outside a request (a background job that skips the middleware), bind a tenant
explicitly through `ITenantContextInitializer` / `ITenantServiceScopeFactory`.

**Non-singleton overrides are rejected.** The factory validates each override
delegate's registrations and throws `InvalidOperationException` for any `Scoped`
or `Transient` descriptor, naming the offending service type. Register only
singletons in `TenantOverrides` and `DynamicOverrides`; put request-scoped
services in `ForAll`.

**`UseHostResolver` requires tenant host names in the database.** The host
resolver queries `ITenantManager<TTenant>.FindByHost` for a tenant whose host
matches the incoming `Host` header, and throws `TenantResolveException` when
none matches. Only one resolver is active per host — there is no fall-through
chain.

## See also

- [Multi-tenancy guide](../guides/multi-tenancy.md) — `UseTenancy` basics and
  single-resolver setup
- [Tenancy document](../documents/tenancy.md) — per-tenant DI internals,
  `TenantCompositeServiceProvider`, resolver pipeline
- [Identity guide](../guides/identity.md) — setting the `Tenant` claim on the
  principal for `UsePrincipalResolver`
