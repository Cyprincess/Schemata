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

| Method                   | Source                                          | Header / Parameter |
| ------------------------ | ----------------------------------------------- | ------------------ |
| `UseHeaderResolver()`    | HTTP request header                             | `x-tenant-id`      |
| `UseHostResolver()`      | `Host` header matched against tenant host names | (none)             |
| `UsePathResolver()`      | Route parameter                                 | `{Tenant}`         |
| `UsePrincipalResolver()` | Authenticated user claim                        | `Tenant`           |
| `UseQueryResolver()`     | Query string parameter                          | `Tenant`           |

Only the first `UseXxxResolver()` call wins — later calls are ignored, and the accessor asks a single `ITenantResolver` once per request. For "header overrides path" semantics, implement a composite `ITenantResolver` and register it directly.

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

The default `SchemataTenantManager<Tenant>` resolves these repositories to look up the identifier
from the selected resolver. Replace `Tenant` with `SchemataTenant` when you use the non-generic
`UseTenancy()` overload.

## Per-tenant DI overrides

`ForAll` and `ForTenant` on the builder register services that participate in tenant resolution. They have very different lifetime contracts:

| Method                                         | Where the registrations land                                                                        | Allowed lifetimes                                                                                                                              |
| ---------------------------------------------- | --------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `ForAll(configure)`                            | Root `IServiceCollection`                                                                           | Any (Singleton / Scoped / Transient) — these become normal host services that every tenant sees through the composite provider's root fallback |
| `ForTenant(tenantId, configure)`               | Per-tenant override container, applied at provider build time                                       | **Singleton only**                                                                                                                             |
| `ForTenant((tenantId, services, root) => ...)` | Same as above but applied to every tenant container, with the tenant id and root provider available | **Singleton only**                                                                                                                             |

Scoped or transient registrations in either `ForTenant` overload throw `InvalidOperationException` at provider-build time. `ForAll` adds host registrations visible to every tenant; a host service's constructor still resolves from the host container, so tenant-aware host services must consult `ITenantContextAccessor<TTenant>` while performing their work.

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
public sealed class StudentService(ITenantContextAccessor<Tenant> accessor)
{
    public string? GetTenantName() => accessor.Tenant?.DisplayName;
}
```

The `Tenant` property is `null` until middleware initialization completes for the current request.

## Verify

Seed the tenant before sending a request:

```csharp
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
