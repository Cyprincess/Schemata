# Multi-Tenancy

Scope each request to a specific tenant and resolve downstream services from a tenant-isolated DI container. This
is a feature branch after [gRPC Transport](grpc-transport.md): it works from [Getting Started](getting-started.md)
and does not add a tenant filter to `Student` rows by itself.

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Tenancy.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Tenancy.Foundation
```

## Enable tenancy

Add `UseTenancy()` and pick a resolver:

```csharp
schema.UseTenancy()
      .UseHeaderResolver();
```

`UseTenancy()` uses `SchemataTenant` as the default tenant entity. On each request, the tenancy middleware resolves the tenant and swaps the request's service provider for a tenant-scoped one for the duration of the request. Register repositories for the tenant entity and `SchemataTenantHost` so the default tenant manager can resolve them. The middleware position and feature ordering are covered in [Tenancy](../documents/tenancy.md).

## Choose a resolver

Five built-in resolver strategies ship with the foundation:

| Method                   | Stage     | Source                                          | Header / Parameter |
| ------------------------ | --------- | ----------------------------------------------- | ------------------ |
| `UseHeaderResolver()`    | Request   | HTTP request header                             | `x-tenant-id`      |
| `UseHostResolver()`      | Request   | `Host` header matched against tenant host names | (none)             |
| `UsePathResolver()`      | Request   | Route parameter                                 | `{Tenant}`         |
| `UsePrincipalResolver()` | Principal | Authenticated user claim                        | `Tenant`           |
| `UseQueryResolver()`     | Request   | Query string parameter                          | `Tenant`           |

Each `UseXxxResolver()` registers its resolver into the same `IEnumerable<ITenantResolver>`
collection. Request-stage resolvers run during `SchemataTenancyMiddleware`; Principal-stage
resolvers run once after `SchemataTenantPrincipalMiddleware` (post-default authentication) and
again from `TenantPolicyEvaluator.AuthenticateAsync` after each authorization policy's
authentication scheme resolves, so custom Principal-stage resolvers must be idempotent across
these calls. Multiple sources compose — a header, a path, and a query resolver can all be
installed and contribute to the resolved identity. When two Request-stage resolvers return
different non-null ids the accessor raises `TenantResolveException`; a Principal-stage
resolver disagrees with a non-null request-stage tenant also throws. When the request stage
produced no tenant and the principal carries a `Tenant` claim, the principal resolver promotes
the request to a tenant scope (the standard "header-or-claim" pattern). Use
`RequestPrincipalResolver` together with a Request-stage resolver only when both sources are
expected to agree (the principal's `Tenant` claim is typically derived from the same header
that the request-stage resolver reads).

## Custom tenant entity

`SchemataTenant` carries `Uid` (Guid primary key), `Name`, `CanonicalName`, `DisplayName` / `DisplayNames`, `Description` / `Descriptions`, `Timestamp`, `CreateTime`, `UpdateTime`, and a `Hosts` navigation to `SchemataTenantHost`. Add tenant-specific data by subclassing:

```csharp
using Schemata.Tenancy.Skeleton.Entities;

public class Tenant : SchemataTenant
{
    public string? Plan { get; set; }
}
```

Pass the custom type when enabling tenancy:

```csharp
schema.UseTenancy<Tenant>()
      .UseHeaderResolver();
```

Register the custom tenant and host repositories in the existing `ConfigureServices` callback. They
reuse the `AppDbContext` factory already registered for `Student`:

```csharp
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Tenancy.Skeleton.Entities;

schema.ConfigureServices(services => {
    services.AddRepository<Tenant, EfCoreRepository<AppDbContext, Tenant>>();
    services.AddRepository<SchemataTenantHost, EfCoreRepository<AppDbContext, SchemataTenantHost>>();
});
```

The default `SchemataTenantManager<Tenant>` dispatches its lookups to the closed handlers that resolve
these repositories. Replace `Tenant` with `SchemataTenant` when you use the non-generic
`UseTenancy()` overload.

## Per-tenant DI overrides

`ForAll` and `ForTenant` on the builder register services that participate in tenant resolution. They have very different lifetime contracts:

| Method                                         | Where the registrations land                                                                        | Allowed lifetimes                                                                                                                              |
| ---------------------------------------------- | --------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `ForAll(configure)`                            | Root `IServiceCollection`                                                                           | Any (Singleton / Scoped / Transient) — these become normal host services that every tenant sees through the composite provider's root fallback |
| `ForTenant(tenantId, configure)`               | Per-tenant override container, applied at provider build time                                       | Singleton / Scoped / Transient; open-generic descriptors raise `InvalidOperationException`                                                     |
| `ForTenant((tenantId, services, root) => ...)` | Same as above but applied to every tenant container, with the tenant id and root provider available | Singleton / Scoped / Transient; open-generic descriptors raise `InvalidOperationException`                                                     |

`ForAll` adds host registrations visible to every tenant. Inside the per-tenant composite provider,
`TenantCompositeServiceProvider.GetService` falls back to the host scope's provider when the
tenant overrides do not register the type — so a host service receives the host scope's
`SchemataTenantContextAccessor`, which is never initialized for the current request and reads
`null` from `accessor.Tenant`. Host services resolve the request's tenant identity through the
ambient `TenantContext.Current` (set by `SchemataTenancyMiddleware` and refreshed by the
principal and execution middlewares); services that need tenant-side dependencies must be
registered explicitly with `ForTenant(...)`.

```csharp
public interface IFeatureGate
{
    bool IsEnabled(string feature);
}

public sealed class AcmeFeatureGate : IFeatureGate
{
    public bool IsEnabled(string feature) => feature == "advanced-reporting";
}

schema.UseTenancy<Tenant>()
      .ForTenant("00000000-0000-0000-0000-000000000001", overrides => {
          overrides.AddSingleton<IFeatureGate, AcmeFeatureGate>();
      })
      .UseHeaderResolver();
```

Lookups hit the per-tenant overrides first, then fall through to the host root. This gives the Acme tenant a distinct `IFeatureGate`; it does not replace dependencies captured by root-registered repositories. Add row ownership, a tenant discriminator, or an application-specific context factory when Student data itself needs isolation. Tenant providers are cached with bounded capacity and sliding expiration; the composite-provider internals and cache tuning options are in [Tenancy](../documents/tenancy.md).

## Access the current tenant

Inject the generic accessor anywhere to read the resolved tenant:

```csharp
using Schemata.Tenancy.Skeleton;

public sealed class StudentService(ITenantContextAccessor<Tenant> accessor)
{
    public string? GetTenantName() => accessor.Tenant?.DisplayName;
}
```

The `Tenant` property is `null` until middleware initialization completes for the current request.

## Verify

Seed the tenant before sending a request:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton;

using var scope = app.Services.CreateScope();
var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<Tenant>>();
var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

if (await manager.FindByTenantId(tenantId, default) is null)
{
    await manager.CreateAsync(new Tenant {
        Uid = tenantId,
        Name = tenantId.ToString("N"),
    }, default);
}
```

```shell
dotnet run
```

```shell
# Resolve a tenant-scoped service for the configured tenant
curl http://localhost:5000/v1/students \
     -H "x-tenant-id: 00000000-0000-0000-0000-000000000001"
```

After you seed a `Tenant` with this `Uid`, the request resolves that tenant and the `AcmeFeatureGate` is available from the request service provider. The base Student repository still uses its configured database until you add an isolation strategy.

## Next steps

- [Flow](flow.md) — use the resolved tenant context in a BPMN application
- [Event Bus](event-bus.md) — publish events from tenant-aware services
- [gRPC Transport](grpc-transport.md) — tenant resolution works the same on gRPC

## See also

- [Tenancy](../documents/tenancy.md) — per-tenant DI, resolver architecture, `ITenantContextAccessor`
- [Multi-Tenant Setup](../cookbook/multi-tenant-cookbook.md) — combined resolvers and per-tenant DI overrides
