# Multi-Tenancy

This guide adds tenant resolution and per-tenant data isolation to the Student API. After completing it, each request will be scoped to a specific tenant and downstream services will resolve from a tenant-isolated DI container.

## Add the tenancy package

```shell
dotnet add package --prerelease Schemata.Tenancy.Foundation
```

## Enable tenancy

Add `UseTenancy()` to the Schemata builder in `Program.cs` and chain a resolver. The header resolver reads the tenant identifier from the `x-tenant-id` HTTP request header:

```csharp
schema.UseTenancy()
      .UseHeaderResolver();
```

`UseTenancy()` registers the default `SchemataTenant<Guid>` entity type with `Guid` keys. It adds two pieces of middleware to the request pipeline automatically:

1. `SchemataTenantContextAccessorInitializer` -- calls `ITenantResolver<TKey>.ResolveAsync` to find the tenant identifier, then loads the `SchemataTenant` from the `ITenantManager` and stores it in `ITenantContextAccessor`.
2. `SchemataTenantServiceProviderReplacer` -- swaps the request `IServiceProvider` with a tenant-scoped container so all downstream services resolve in tenant isolation.

## Choose a resolver

Schemata ships five built-in resolver strategies. Register exactly one:

| Method                   | Source                                          | Header / Parameter |
| ------------------------ | ----------------------------------------------- | ------------------ |
| `UseHeaderResolver()`    | HTTP request header                             | `x-tenant-id`      |
| `UseHostResolver()`      | `Host` header matched against tenant host names | --                 |
| `UsePathResolver()`      | Route parameter                                 | `{Tenant}`         |
| `UsePrincipalResolver()` | Authenticated user claim                        | `Tenant`           |
| `UseQueryResolver()`     | Query string parameter                          | `Tenant`           |

Each resolver implements `ITenantResolver<TKey>`, which has a single method:

```csharp
Task<TKey?> ResolveAsync(CancellationToken ct);
```

## Define a custom tenant entity

The default `SchemataTenant<Guid>` provides `TenantId`, `Hosts`, `Name`, `CanonicalName`, `DisplayName`, and timestamp fields. To add tenant-specific data, subclass it:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Tenancy.Skeleton.Entities;

[Table("Tenants")]
public class Tenant : SchemataTenant<Guid>
{
    public string? Plan { get; set; }
}
```

Then pass the custom type when enabling tenancy:

```csharp
schema.UseTenancy<Tenant, Guid>()
      .UseHeaderResolver();
```

## Add tenant to the DbContext

Register the tenant entity in your `AppDbContext`:

```csharp
public DbSet<Tenant> Tenants => Set<Tenant>();
```

## Configure per-tenant data isolation

The `UseTenancy` call accepts a configure delegate that runs once per tenant when its isolated DI container is first built. Use this to register tenant-specific service overrides -- for example, pointing each tenant at a separate database:

```csharp
schema.UseTenancy<Tenant, Guid>((services, tenant) => {
          services.AddDbContext<AppDbContext>(options => {
              options.UseSqlite($"Data Source=tenant_{tenant?.TenantId}.db");
          });
      })
      .UseHeaderResolver();
```

The `SchemataTenantServiceProviderFactory` copies the root service collection and applies the configure delegate for each tenant. The resulting `IServiceProvider` is cached per tenant for the application lifetime.

## Access the current tenant

Inject `ITenantContextAccessor` (or the generic `ITenantContextAccessor<TTenant, TKey>`) anywhere in your application to read the resolved tenant:

```csharp
public class StudentService(ITenantContextAccessor accessor)
{
    public string? GetTenantName() => accessor.Tenant?.DisplayName;
}
```

The `Tenant` property is `null` until middleware initialization completes, so it is only available after the tenant context accessor initializer runs in the request pipeline.

## Verify

```shell
dotnet run
```

Create a tenant record in your database, then pass its `TenantId` in the header:

```shell
# Create a student under a specific tenant
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -H "x-tenant-id: 00000000-0000-0000-0000-000000000001" \
     -d '{"full_name":"Alice","age":20}'

# List students -- only returns students for this tenant
curl http://localhost:5000/students \
     -H "x-tenant-id: 00000000-0000-0000-0000-000000000001"
```

Requests without the `x-tenant-id` header skip tenant resolution (the accessor's `Tenant` property remains `null`). Requests with an invalid or unparseable tenant identifier cause a `TenantResolveException`.

## Next steps

- [Workflow](workflow.md) -- add an enrollment state machine to the Student entity
- For the full API surface and architecture details, see [Tenancy](../documents/tenancy.md)
