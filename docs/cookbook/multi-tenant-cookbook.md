# Multi-Tenant Setup

## What you'll build

A multi-tenant API where each request is resolved to a tenant using a
combination of resolvers: an `x-tenant-id` header for machine clients, a
`{Tenant}` route segment for browser-friendly URLs, and a `Tenant` claim for
authenticated users. You will configure tenant-specific singleton services and
identify the separate repository design needed for tenant data isolation.

## Prerequisites

- Completed [Getting Started](../guides/getting-started.md).
- `Schemata.Tenancy.Foundation` package added.

```shell
dotnet add package --prerelease Schemata.Tenancy.Foundation
```

## Step 1 — Register the tenancy feature

Define the tenant type, then call `UseTenancy<TTenant>()` and pick one resolver:

```csharp
using Schemata.Tenancy.Skeleton.Entities;

public class AppTenant : SchemataTenant
{
    public string? ConnectionString { get; set; }
}

var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseJsonSerializer();

        var tenancy = schema.UseTenancy<AppTenant>()
                            .UseHeaderResolver();

        schema.ConfigureServices(services => {
            services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));
            services.AddRepository<AppTenant, EfCoreRepository<AppDbContext, AppTenant>>();
            services.AddRepository<SchemataTenantHost, EfCoreRepository<AppDbContext, SchemataTenantHost>>();
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

| Method                   | Resolver                       | Source                                          |
| ------------------------ | ------------------------------ | ----------------------------------------------- |
| `UseHeaderResolver()`    | `RequestHeaderResolver`        | `x-tenant-id` request header                    |
| `UseHostResolver()`      | `RequestHostResolver<TTenant>` | `Host` header matched against tenant host names |
| `UsePathResolver()`      | `RequestPathResolver`          | `{Tenant}` route parameter                      |
| `UsePrincipalResolver()` | `RequestPrincipalResolver`     | `Tenant` claim on the authenticated principal   |
| `UseQueryResolver()`     | `RequestQueryResolver`         | `Tenant` query string parameter                 |

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
schema.UseTenancy<AppTenant>();   // The composite is already in DI; built-in resolver calls would be ignored.
```

**Verify:** After you seed an `AppTenant` row with the identifier, start the app and send a request
with `x-tenant-id: <guid>`. The middleware resolves the tenant and makes it available via
`ITenantContextAccessor<AppTenant>`.

## Step 2 — Inspect the custom tenant entity

`AppTenant` inherits the default `Uid`, `Name`, and `CanonicalName` fields and
adds the connection string that an application-specific data-isolation layer
can use. `UseTenancy<TTenant>()` uses `SchemataTenantManager<TTenant>` as the
default manager. To supply a custom manager, use the three-argument overload:
`UseTenancy<TManager, TTenant>()`. Only one `ITenantResolver` is active per
host; stacking `UseHeaderResolver().UsePathResolver()` does not chain them —
the first wins.

**Verify:** `ITenantContextAccessor<AppTenant>.Tenant` resolves to an
`AppTenant` instance with the `ConnectionString` property populated.

## Step 3 — Configure per-tenant DI overrides

The tenancy system builds one `IServiceProvider` per tenant and caches it.
Tenant-specific singletons are registered through `ForTenant`:

```csharp
public interface IFeatureGate
{
    bool IsEnabled(string feature);
}

public sealed class PremiumFeatureGate : IFeatureGate
{
    public bool IsEnabled(string feature) => feature == "advanced-reporting";
}

public sealed class DefaultFeatureGate : IFeatureGate
{
    public bool IsEnabled(string feature) => false;
}

tenancy.ForAll(services => {
           services.AddSingleton<IFeatureGate, DefaultFeatureGate>();
       })
       .ForTenant("00000000-0000-0000-0000-000000000001", services => {
           services.AddSingleton<IFeatureGate, PremiumFeatureGate>();
       });
```

`TenantCompositeServiceProvider` resolves services from the tenant-specific
container first, then falls back to the host root. Root-registered repositories
construct their dependencies from the host container, so a tenant override does
not replace their `IDbContextFactory`. Isolate Student data with a tenant
discriminator, or implement a root context factory that chooses a connection at
`CreateDbContext` time while tenant catalog repositories remain on a shared
control-plane database.

**Important:** Tenant overrides must be registered as `Singleton`. The factory
enforces this at build time and throws `InvalidOperationException` if a
`Scoped` or `Transient` service is added to the overrides collection.

**Verify:** A request for the configured tenant resolves `IFeatureGate` as
`PremiumFeatureGate`; a request for another tenant falls back to the host
registration.

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

**Verify:** `GET /acme/students` and `GET /beta/students` resolve different
tenant contexts when matching tenant rows exist. Database isolation needs the
tenant-aware repository design described in Step 3.

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
