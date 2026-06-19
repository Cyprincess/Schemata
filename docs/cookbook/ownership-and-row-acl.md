# Ownership and Row ACL

## What you'll build

A resource API where every entity is stamped with the canonical name of the
principal who created it, and every query is automatically filtered to return
only that principal's rows. You'll implement `IOwnable` on the `Student`
entity from [Getting Started](../guides/getting-started.md), register
`UseOwner()` on the repository builder, and provide an `IOwnerResolver` that
reads the authenticated user's subject claim.

## Prerequisites

- Completed [Getting Started](../guides/getting-started.md) — the `Student`
  entity and EF Core repository must already be wired up.
- `Schemata.Entity.Owner` package added.
- An authentication scheme configured (e.g., via `UseIdentity` or bearer
  tokens) so `HttpContext.User` carries a `sub` claim.

```shell
dotnet add package --prerelease Schemata.Entity.Owner
```

## Step 1 — Implement `IOwnable` on the entity

Add `IOwnable` to `Student` and expose the `Owner` property:

```csharp
using Schemata.Abstractions.Entities;

[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete, IOwnable
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    // IOwnable — canonical name of the principal who owns this row
    public string? Owner { get; set; }

    // IIdentifier
    public Guid Uid { get; set; }

    // ICanonicalName
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    // ITimestamp
    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    // ISoftDelete
    public DateTime? DeleteTime { get; set; }
    public DateTime? PurgeTime  { get; set; }
}
```

`IOwnable` requires a single `string? Owner` property. The value is the
canonical name of the owning principal, e.g. `"users/alice"`. It is set
automatically by `AdviceAddOwner` at insert time; you never set it from
application code.

**Verify:** `dotnet ef migrations add AddOwner` produces a migration that adds
an `Owner` column to the `Students` table.

## Step 2 — Register `UseOwner()` on the repository builder

`UseOwner()` is an extension on `SchemataRepositoryBuilder`, not on
`SchemataBuilder`. Call it after `AddRepository`:

```csharp
schema.ConfigureServices(services => {
    services.AddRepository(typeof(EfCoreRepository<,>))
            .UseEntityFrameworkCore<AppDbContext>(
                (_, opts) => opts.UseSqlite("Data Source=app.db"))
            .UseOwner();
});
```

`UseOwner()` registers two open-generic advisors as `Scoped`:

| Advisor | Pipeline | What it does |
| --- | --- | --- |
| `AdviceAddOwner<TEntity>` | Add | Calls `IOwnerResolver<TEntity>.ResolveAsync`, sets `IOwnable.Owner` |
| `AdviceBuildQueryOwner<TEntity>` | BuildQuery | Appends `WHERE Owner = <resolved>` to every query |

Both advisors check `IOwnable` at runtime — entities that don't implement it
are skipped. `AdviceAddOwner` runs after `AdviceAddCanonicalName` (its
`Order` is `AdviceAddCanonicalName.DefaultOrder + 10_000_000`).
`AdviceBuildQueryOwner` runs after `AdviceBuildQuerySoftDelete` (its `Order`
is `AdviceBuildQuerySoftDelete.DefaultOrder + 10_000_000`).

**Verify:** Start the app. A `POST /students` request without authentication
throws `AuthorizationException` (the default `OnNullOwner` policy is
`Reject`). This confirms the advisor is active.

## Step 3 — Implement `IOwnerResolver<Student>`

The advisors call `IOwnerResolver<TEntity>.ResolveAsync` to determine the
owner. Implement it to return the authenticated user's canonical name:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Schemata.Entity.Owner;

public sealed class HttpContextOwnerResolver<TEntity> : IOwnerResolver<TEntity>
{
    private readonly IHttpContextAccessor _http;

    public HttpContextOwnerResolver(IHttpContextAccessor accessor)
    {
        _http = accessor;
    }

    public ValueTask<string?> ResolveAsync(CancellationToken ct)
    {
        var sub = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
            return ValueTask.FromResult<string?>(null);

        // Return a canonical name, e.g. "users/alice"
        return ValueTask.FromResult<string?>($"users/{sub}");
    }
}
```

Register it as an open-generic scoped service so it covers all entity types:

```csharp
schema.ConfigureServices(services => {
    services.AddHttpContextAccessor();
    services.TryAddScoped(typeof(IOwnerResolver<>),
                          typeof(HttpContextOwnerResolver<>));
});
```

**Verify:** Authenticate as `alice` and `POST /students`. The created row has
`Owner = "users/alice"`. Authenticate as `bob` and `GET /students` — only
Bob's rows appear.

## Step 4 — Configure the null-owner policy

When `ResolveAsync` returns null (unauthenticated request), the default policy
is `OnNullOwnerPolicy.Reject`:

- `AdviceAddOwner` throws `AuthorizationException`.
- `AdviceBuildQueryOwner` throws `AuthorizationException`.

To return an empty result instead of an error for unauthenticated queries,
change the policy:

```csharp
schema.ConfigureServices(services => {
    services.Configure<SchemataOwnerOptions>(o => {
        o.OnNullOwner = OnNullOwnerPolicy.EmptyResult;
    });
});
```

The three policies are:

| Policy | Add behavior | Query behavior |
| --- | --- | --- |
| `Reject` (default) | Throws `AuthorizationException` | Throws `AuthorizationException` |
| `EmptyResult` | Returns `AdviseResult.Block` | Applies `WHERE 1=0` |
| `AllowAll` | Leaves `Owner` unset | No filter applied |

`AllowAll` is only safe when authorization is enforced upstream by other means.

**Verify:** With `EmptyResult`, an unauthenticated `GET /students` returns an
empty list with HTTP 200 instead of 401.

## Step 5 — Suppress ownership for admin queries

Some operations need to bypass the owner filter, for example an admin endpoint
that lists all students. Use `SuppressQueryOwner()` on the repository:

```csharp
public class AdminStudentService(IRepository<Student> repository)
{
    public async Task<List<Student>> ListAllAsync(CancellationToken ct)
    {
        using (repository.SuppressQueryOwner())
        {
            return await repository.ListAsync<Student>(null, ct).ToListAsync(ct);
        }
    }
}
```

`SuppressQueryOwner()` sets `QueryOwnerSuppressed` in the `AdviceContext` and
returns an `IDisposable`. The `using` scope restores the prior state on exit.
`AdviceBuildQueryOwner` checks `ctx.Has<QueryOwnerSuppressed>()` and skips the
filter while the marker is present.

To suppress owner stamping on insert (e.g., seeding data), scope
`SuppressOwner()`:

```csharp
using (repository.SuppressOwner())
{
    await repository.AddAsync(entity, ct);
}
```

**Verify:** `ListAllAsync` returns rows from all owners. A normal
`repository.ListAsync` still filters by the current principal.

## Common pitfalls

**`UseOwner()` lives on `SchemataRepositoryBuilder`.** Chain it after
`AddRepository(...)`. Calling `schema.UseOwner()` directly on `SchemataBuilder`
does not compile.

**`IOwnerResolver<TEntity>` must be registered.** `AdviceAddOwner` and
`AdviceBuildQueryOwner` both inject `IOwnerResolver<TEntity>`. If no resolver
is registered, DI throws at the first request that touches an `IOwnable`
entity. Register a resolver for every entity type, or use an open-generic
registration as shown in Step 3.

**`AdviceAddOwner` leaves an already-set `Owner` untouched.** Its
`!string.IsNullOrEmpty(ownable.Owner)` guard returns early when `Owner` is
already populated, so a value present on the entity before the advisor runs is
preserved — the resolver does not override it. If untrusted input can reach the
entity directly (bypassing request handling that clears the field), clear
`Owner` yourself before `AddAsync` to force the resolver's value.

**Per-entity resolvers override the open-generic registration.** If you
register `IOwnerResolver<Student>` explicitly, it takes precedence over the
open-generic `IOwnerResolver<>` for `Student` only. Use this to apply
different ownership strategies per entity type.

## See also

- [Getting Started](../guides/getting-started.md) — `Student` entity baseline
- [Access Control guide](../guides/access-control.md) — `UseSecurity` and
  `IAccessProvider` for role-based checks
- [Repository ownership document](../documents/repository/ownership.md) —
  advisor internals and `SchemataOwnerOptions`
- [Traits document](../documents/entity/traits.md) — `IOwnable` and all other
  entity traits
