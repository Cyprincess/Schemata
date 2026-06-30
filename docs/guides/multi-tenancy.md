# Multi-Tenancy

Scope each request to a specific tenant and resolve downstream services from a tenant-isolated DI container. This guide builds on [Getting Started](getting-started.md).

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

`UseTenancy()` uses `SchemataTenant` as the default tenant entity. On each request, the tenancy middleware resolves the tenant and swaps the request's service provider for a tenant-scoped one for the duration of the request. The middleware position and feature ordering are covered in [Tenancy](../documents/tenancy.md).

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

## Per-tenant data isolation

`ForAll` and `ForTenant` on the builder register services that participate in tenant resolution. They have very different lifetime contracts:

| Method                                         | Where the registrations land                                                                        | Allowed lifetimes                                                                                                                              |
| ---------------------------------------------- | --------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `ForAll(configure)`                            | Root `IServiceCollection`                                                                           | Any (Singleton / Scoped / Transient) — these become normal host services that every tenant sees through the composite provider's root fallback |
| `ForTenant(tenantId, configure)`               | Per-tenant override container, applied at provider build time                                       | **Singleton only**                                                                                                                             |
| `ForTenant((tenantId, services, root) => ...)` | Same as above but applied to every tenant container, with the tenant id and root provider available | **Singleton only**                                                                                                                             |

Scoped or transient registrations in either `ForTenant` overload throw `InvalidOperationException` at provider-build time. Per-tenant services that need to participate in the request-scope lifecycle (`AddDbContext`, repositories, etc.) belong in `ForAll`; they pick up the right tenant by consulting `ITenantContextAccessor<TTenant>` at construction time.

```csharp
schema.UseTenancy<Tenant>()
      .ForAll(services => {
          services.AddDbContext<AppDbContext>((sp, opts) => {
              var tenant = sp.GetRequiredService<ITenantContextAccessor<Tenant>>().Tenant;
              var conn   = tenant?.Plan == "premium"
                  ? "Data Source=premium.db"
                  : "Data Source=shared.db";
              opts.UseSqlite(conn);
          });
      })
      .ForTenant("00000000-0000-0000-0000-000000000001", overrides => {
          overrides.AddSingleton<IFeatureGate, AcmeFeatureGate>();
      })
      .UseHeaderResolver();
```

Lookups hit the per-tenant overrides first, then fall through to the host root. Tenant providers are cached with bounded capacity and sliding expiration; the composite-provider internals and cache tuning options are in [Tenancy](../documents/tenancy.md).

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

```shell
dotnet run
```

```shell
# Create a student under a specific tenant
curl -X POST http://localhost:5000/v1/students \
     -H "Content-Type: application/json" \
     -H "x-tenant-id: 00000000-0000-0000-0000-000000000001" \
     -d '{"full_name":"Alice","age":20}'

# List students — only returns students for this tenant
curl http://localhost:5000/v1/students \
     -H "x-tenant-id: 00000000-0000-0000-0000-000000000001"
```

Requests without the `x-tenant-id` header skip tenant resolution; `accessor.Tenant` stays `null` and the request runs against the host root provider.

## Next steps

- [Flow](flow.md) — add a BPMN process to the tenant-scoped Student entity
- [Event Bus](event-bus.md) — publish events from per-tenant services
- [gRPC Transport](grpc-transport.md) — tenant resolution works the same on gRPC

## See also

- [Tenancy](../documents/tenancy.md) — per-tenant DI, resolver architecture, `ITenantContextAccessor`
- [Multi-Tenant Setup](../cookbook/multi-tenant-cookbook.md) — combined resolvers and per-tenant DI overrides
