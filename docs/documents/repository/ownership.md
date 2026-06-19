# Repository Ownership

The `Schemata.Entity.Owner` package adds automatic owner assignment and owner-scoped query filtering to the repository layer. Entities implementing `IOwnable` are automatically stamped with the current principal's canonical name on create, and queries are automatically filtered to include only entities owned by that principal.

Ownership is opt-in. Call `UseOwner()` on the repository builder to enable the advisors.

## Where the code lives

| Item | Path |
|---|---|
| `IOwnerResolver<TEntity>` | `src/Schemata.Entity.Owner/IOwnerResolver.cs` |
| `SchemataOwnerOptions` | `src/Schemata.Entity.Owner/SchemataOwnerOptions.cs` |
| `AdviceAddOwner<TEntity>` | `src/Schemata.Entity.Owner/Advisors/AdviceAddOwner.cs` |
| `AdviceBuildQueryOwner<TEntity>` | `src/Schemata.Entity.Owner/Advisors/AdviceBuildQueryOwner.cs` |
| `UseOwner` extension | `src/Schemata.Entity.Owner/Extensions/SchemataRepositoryBuilderExtensions.cs` |

## IOwnerResolver

```csharp
public interface IOwnerResolver<TEntity>
{
    ValueTask<string?> ResolveAsync(CancellationToken ct);
}
```

`IOwnerResolver<TEntity>` resolves the canonical name of the principal that should own entities of type `TEntity` for the current request. The interface is entity-typed so different entity types can use different resolution strategies (e.g., tenant-wide ownership for shared entities, user-scoped ownership for personal entities).

## Advisors

### AdviceAddOwner

`AdviceAddOwner<TEntity>` implements `IRepositoryAddAdvisor<TEntity>`. It runs during the add mutation pipeline and populates `IOwnable.Owner`.

**Order:** `AdviceAddCanonicalName.DefaultOrder + 10_000_000` = 230,000,000. Runs after `AdviceAddCanonicalName` so the entity's own canonical name is settled before the owner is assigned.

**Behavior:**

1. If `OwnerSuppressed` is in the advice context, skips.
2. If the entity does not implement `IOwnable`, skips.
3. If `IOwnable.Owner` is already non-null/non-empty, skips — callers can override the default by pre-setting the owner.
4. Calls `IOwnerResolver<TEntity>.ResolveAsync`. If the result is non-empty, assigns it to `IOwnable.Owner` and returns `Continue`.
5. When the resolver returns `null`, behavior is governed by `SchemataOwnerOptions.OnNullOwner`:
   - `Reject` (default) — throws `AuthorizationException`.
   - `EmptyResult` — returns `Block`.
   - `AllowAll` — returns `Continue`.

### AdviceBuildQueryOwner

`AdviceBuildQueryOwner<TEntity>` implements `IRepositoryBuildQueryAdvisor<TEntity>`. It applies a global query filter restricting results to entities owned by the current caller.

**Order:** `AdviceBuildQuerySoftDelete.DefaultOrder + 10_000_000` = 110,000,000.

**Behavior:**

1. If `QueryOwnerSuppressed` is in the advice context, skips.
2. If `TEntity` does not implement `IOwnable`, skips.
3. Calls `IOwnerResolver<TEntity>.ResolveAsync`. If non-empty, appends `.OfType<IOwnable>().Where(e => e.Owner == owner).OfType<TEntity>()`.
4. When the resolver returns `null`, applies `SchemataOwnerOptions.OnNullOwner` (same policy as `AdviceAddOwner`).

## Registration

```csharp
services.AddRepository(typeof(EfCoreRepository<,>))
        .UseOwner();
```

`UseOwner()` registers `SchemataOwnerOptions`, `AdviceBuildQueryOwner<>` as `IRepositoryBuildQueryAdvisor<>`, and `AdviceAddOwner<>` as `IRepositoryAddAdvisor<>`. All registrations use `TryAddEnumerable` so they don't displace custom advisors.

You must also register a concrete `IOwnerResolver<TEntity>`. A typical implementation reads the current principal from `IHttpContextAccessor`:

```csharp
services.AddScoped(typeof(IOwnerResolver<>), typeof(PrincipalOwnerResolver<>));
```

```csharp
public sealed class PrincipalOwnerResolver<TEntity> : IOwnerResolver<TEntity>
{
    private readonly IHttpContextAccessor _accessor;

    public PrincipalOwnerResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    public ValueTask<string?> ResolveAsync(CancellationToken ct)
    {
        var owner = _accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return new(owner);
    }
}
```

## Suppression

| Method | Marker | Effect |
|---|---|---|
| `repository.SuppressOwner()` | `OwnerSuppressed` | Skips `AdviceAddOwner` for this instance. |
| `repository.SuppressQueryOwner()` | `QueryOwnerSuppressed` | Skips `AdviceBuildQueryOwner` for this instance. |

```csharp
// Add without assigning ownership
using (repository.SuppressOwner())
{
    await repository.AddAsync(entity, ct);
}

// List all entities regardless of owner
using (repository.SuppressQueryOwner())
{
    var all = await repository.ListAsync<Document>(null, ct).ToListAsync(ct);
}
```

## Override and bypass

`AdviceAddOwner` leaves an already-set `Owner` untouched, so a caller that pre-assigns ownership keeps
control. To create an entity with no owner stamping at all, scope `SuppressOwner()`. `AdviceAddOwner`
fires whether the entity reaches the repository through the resource layer or directly.

## Extension points

- **Custom `OnNullOwner` policy**: configure `SchemataOwnerOptions.OnNullOwner` to `AllowAll` for background jobs that run without a principal.
- **Per-entity resolver**: register `IOwnerResolver<MyEntity>` with a closed-generic registration to override the open-generic fallback for a specific entity type.

## Design motivation

Ownership filtering is simple equality (`Owner == resolvedOwner`). It requires no custom LINQ expression and no per-entity provider. For complex access policies (role checks, attribute-based access, hierarchical ownership), use `IEntitlementProvider` from `Schemata.Security.Skeleton` instead.

## See also

- [entity/traits.md](../entity/traits.md) — `IOwnable` interface definition
- [query-pipeline.md](query-pipeline.md) — `AdviceBuildQueryOwner` in the build-query stage
- [mutation-pipeline.md](mutation-pipeline.md) — `AdviceAddOwner` in the add pipeline
