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

`UseTenancy()` installs `SchemataTenancyFeature` at priority
`SchemataCorsFeature.DefaultPriority + 5_000_000 = 205_000_000` — after routing and CORS,
before authentication. Its `Order` is `Orders.Max` (900_000_000) so DI registration runs last,
after all other features have had a chance to register their services.

The five available resolvers and their stages are:

| Method                   | Resolver                       | Stage     | Source                                          |
| ------------------------ | ------------------------------ | --------- | ----------------------------------------------- |
| `UseHeaderResolver()`    | `RequestHeaderResolver`        | Request   | `x-tenant-id` request header                    |
| `UseHostResolver()`      | `RequestHostResolver<TTenant>` | Request   | `Host` header matched against tenant host names |
| `UsePathResolver()`      | `RequestPathResolver`          | Request   | `{Tenant}` route parameter                      |
| `UsePrincipalResolver()` | `RequestPrincipalResolver`     | Principal | `Tenant` claim on the authenticated principal   |
| `UseQueryResolver()`     | `RequestQueryResolver`         | Request   | `Tenant` query string parameter                 |

Each `UseXxxResolver()` extension calls
`services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResolver, X>())`, so the accessor
receives an `IEnumerable<ITenantResolver>` and iterates every matching resolver per stage call.
Request-stage resolvers run in `SchemataTenancyMiddleware`; Principal-stage resolvers run once
after `SchemataTenantPrincipalMiddleware` (post-default authentication) and again from
`TenantPolicyEvaluator.AuthenticateAsync` after each authorization policy's authentication
scheme resolves, so custom Principal-stage resolvers must be idempotent across these calls. The
accessor enforces agreement: within a stage every non-null id must match, and a non-null
Principal-stage id must match the non-null request-stage id (otherwise `TenantResolveException`
is raised). When the request stage yields no tenant, a Principal-stage resolver with a non-null
id binds the request — this is the "header-or-claim" pattern where a service accepts either an
`x-tenant-id` header or a `Tenant` claim. To combine several signals ("header + query with
principal override"), install multiple resolvers that produce the same id; for a single
composite that ignores the staging rules, register a custom `ITenantResolver` and skip the
`UseXxxResolver()` extensions:

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
schema.UseTenancy<AppTenant>();   // The custom resolver is already in DI; subsequent UseXxxResolver() calls add to the same collection.
```

**Verify:** After you seed an `AppTenant` row with the identifier, start the app and send a request
with `x-tenant-id: <guid>`. The middleware resolves the tenant and makes it available via
`ITenantContextAccessor<AppTenant>`.

## Step 2 — Inspect the custom tenant entity

`AppTenant` inherits the default `Uid`, `Name`, and `CanonicalName` fields and
adds the connection string that an application-specific data-isolation layer
can use. `UseTenancy<TTenant>()` uses `SchemataTenantManager<TTenant>` as the
default manager. To supply a custom manager, use the three-argument overload:
`UseTenancy<TManager, TTenant>()`. Multiple `ITenantResolver` registrations
compose per stage; stacking `UseHeaderResolver().UsePathResolver()` adds both
resolvers to the collection rather than chaining them.

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

**Important:** Tenant overrides must not register open-generic services. The factory
enforces this at build time and throws `InvalidOperationException` if a descriptor's
`ServiceType` or implementation type carries generic parameters. `Singleton`,
`Scoped`, and `Transient` lifetimes are all accepted.

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

A request to `/acme/students` sets the tenant from the `{Tenant}` segment when
`UsePathResolver()` is registered. To honor both a header and a path segment,
register both `UseHeaderResolver()` and `UsePathResolver()`; the accessor
iterates the collection and requires every non-null answer to match — when
both resolve to the same id the request continues; when they disagree the
accessor raises `TenantResolveException`.

**Verify:** `GET /acme/students` and `GET /beta/students` resolve different
tenant contexts when matching tenant rows exist. Database isolation needs the
tenant-aware repository design described in Step 3.

## Common pitfalls

**Multiple resolvers compose per stage.** `UseXxxResolver()` calls
`TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResolver, X>())`; the accessor
iterates the resulting collection. Request-stage resolvers all run in
`SchemataTenancyMiddleware`; the Principal-stage resolver runs after
authentication. Two Request-stage resolvers that return different ids trigger
`TenantResolveException`; a Principal-stage id that differs from a non-null
request-stage id also triggers `TenantResolveException`. A Principal-stage id
that binds a request that had no request-stage tenant updates `Tenant` and
triggers `BindPrincipalAsync` to reopen the request scope; a Principal-stage
id that matches the request-stage tenant leaves the existing scope untouched
because `BindPrincipalAsync` early-returns when `selected == Identity`.

**`TenantResolveException` per request** — the accessor throws it when a
resolver yields a tenant id that `ITenantManager.FindByTenantId` cannot find,
when a resolver parses a malformed Guid, and when the provider factory is asked
to build a container with no bound tenant. To run a tenant-scoped service
outside a request (a background job that skips the middleware), bind a tenant
explicitly through `ITenantContextInitializer` / `ITenantServiceScopeFactory`.

**Open-generic overrides are rejected.** The factory validates each override
delegate's registrations and throws `InvalidOperationException` for any
descriptor whose `ServiceType` or implementation type carries generic
parameters. Register only closed types in `TenantOverrides` and
`DynamicOverrides`; any lifetime (Singleton / Scoped / Transient) is accepted.

**`UseHostResolver` requires tenant host names in the database.** The host
resolver queries `ITenantManager<TTenant>.FindByHost` for a tenant whose host
matches the incoming `Host` header, and throws `TenantResolveException` when
none matches.

## See also

- [Multi-tenancy guide](../guides/multi-tenancy.md) — `UseTenancy` basics and
  single-resolver setup
- [Tenancy document](../documents/tenancy.md) — per-tenant DI internals,
  `TenantCompositeServiceProvider`, resolver pipeline
- [Identity guide](../guides/identity.md) — setting the `Tenant` claim on the
  principal for `UsePrincipalResolver`
