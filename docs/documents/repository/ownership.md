# Ownership

The `Schemata.Entity.Owner` package adds automatic owner assignment and owner-scoped query filtering to the repository layer. Entities implementing `IOwnable` are automatically stamped with the current principal's canonical name on create, and queries are automatically filtered to include only entities owned by that principal.

Ownership is **opt-in**. Call `UseOwner()` on the repository builder to enable the advisors.

## Package

| Package                 | Dependency                    | Targets                                                 |
| ----------------------- | ----------------------------- | ------------------------------------------------------- |
| `Schemata.Entity.Owner` | `Schemata.Entity.Repository`  | `netstandard2.0`, `netstandard2.1`, `net8.0`, `net10.0` |

## IOwnerResolver

```csharp
public interface IOwnerResolver<TEntity>
{
    ValueTask<string?> ResolveAsync(CancellationToken ct);
}
```

`IOwnerResolver<TEntity>` resolves the canonical name of the principal that should own entities of type `TEntity` for the current request. The interface is entity-typed so different entity types can use different resolution strategies (e.g., tenant-wide ownership for shared entities, user-scoped ownership for personal entities).

A `NullOwnerResolver<TEntity>` is registered as a fallback, always returning `null`. This ensures the owner advisors can be resolved in environments without the resource-layer resolver (background jobs, tests). When `null` is returned, the advisors leave the entity and query untouched.

## Advisors

### AdviceAddOwner

`AdviceAddOwner<TEntity>` implements `IRepositoryAddAdvisor<TEntity>`. It runs during the add mutation pipeline and populates `IOwnable.Owner`.

**Behavior:**

1. If `OwnerSuppressed` is in the advice context, skips.
2. If the entity does not implement `IOwnable`, skips.
3. If `IOwnable.Owner` is already non-null/non-empty, skips — callers can override the default by pre-setting the owner.
4. Calls `IOwnerResolver.ResolveAsync` and assigns the result to `IOwnable.Owner`.

**Order:** After `AdviceAddCanonicalName` (`AdviceAddCanonicalName.DefaultOrder + 10_000_000`, i.e. 130,000,000). This ensures the entity's own canonical name is settled before the owner is assigned.

### AdviceBuildQueryOwner

`AdviceBuildQueryOwner<TEntity>` implements `IRepositoryBuildQueryAdvisor<TEntity>`. It applies a global query filter that restricts results to entities owned by the current caller.

**Behavior:**

1. If `QueryOwnerSuppressed` is in the advice context, skips.
2. If `TEntity` does not implement `IOwnable`, skips.
3. Calls `IOwnerResolver.ResolveAsync`. If the result is null or empty, skips.
4. Calls `container.ApplyModification` to compose `.OfType<IOwnable>().Where(e => e.Owner == owner).OfType<TEntity>()` into the queryable pipeline.

This filter runs before the caller's predicate (the `Func<IQueryable<TEntity>, IQueryable<TResult>>`), so the caller's LINQ composition operates on already-scoped data.

**Order:** After `AdviceBuildQuerySoftDelete` (`AdviceBuildQuerySoftDelete.DefaultOrder + 10_000_000`, i.e. 110,000,000).

## Suppression flags

| Flag                    | Purpose                                                | Set via                        |
| ----------------------- | ------------------------------------------------------ | ------------------------------ |
| `OwnerSuppressed`       | Skips automatic owner assignment on add                | `repository.SuppressOwner()`   |
| `QueryOwnerSuppressed`  | Skips the owner-scoped query filter for one operation  | `repository.SuppressQueryOwner()` |

Both return the repository instance for fluent chaining:

```csharp
// Add an entity without assigning ownership
await repository.Once().SuppressOwner().AddAsync(entity, ct);

// List all entities regardless of owner
var all = repository.Once().SuppressQueryOwner().ListAsync<T>(null);
```

## Configuration

### Enabling ownership

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
    .UseEntityFrameworkCore<AppDbContext>(configure)
    .UseOwner();
```

`UseOwner()`:

1. Registers `NullOwnerResolver<>` as a fallback `IOwnerResolver<>` (scoped, try-add).
2. Registers `AdviceBuildQueryOwner<>` as `IRepositoryBuildQueryAdvisor<>`.
3. Registers `AdviceAddOwner<>` as `IRepositoryAddAdvisor<>`.

### Providing a custom IOwnerResolver

Replace the `NullOwnerResolver` by registering your implementation before (or instead of) `UseOwner()`:

```csharp
services.AddScoped(typeof(IOwnerResolver<>), typeof(PrincipalOwnerResolver<>));
```

The `IServiceProvider` is available via `AdviceContext.ServiceProvider` from within the resolver. A typical implementation extracts the principal from `IHttpContextAccessor`:

```csharp
public sealed class PrincipalOwnerResolver<TEntity> : IOwnerResolver<TEntity>
{
    private readonly IHttpContextAccessor _accessor;

    public PrincipalOwnerResolver(IHttpContextAccessor accessor) => _accessor = accessor;

    public ValueTask<string?> ResolveAsync(CancellationToken ct)
    {
        var principal = _accessor.HttpContext?.User;
        var owner = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return new(owner);
    }
}
```

## Entity setup

Any entity implementing `IOwnable` is automatically handled:

```csharp
public class Document : IIdentifier, IOwnable, ICanonicalName
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public string? Owner { get; set; }
}
```

The advisors detect `IOwnable` at runtime.

## Interaction with Resource Layer

When the resource layer is active (`UseResource()`), the resource request sanitization advisors clear `IOwnable.Owner` on both create and update requests (among other server-managed fields). This prevents clients from claiming ownership. The repository-level `AdviceAddOwner` then assigns the correct owner during persistence.

If you bypass the resource layer and create entities directly via the repository, `AdviceAddOwner` still fires — ownership is enforced at the data layer, not just the HTTP boundary.

## Comparison with IEntitlementProvider

Ownership filtering via `AdviceBuildQueryOwner` is **simple equality** — the entity's `Owner` string must match the resolver's output. It requires no custom LINQ expression and no per-entity provider.

`IEntitlementProvider` (from `Schemata.Security.Skeleton`) is the more general mechanism — it generates arbitrary `Expression<Func<TEntity, bool>>` predicates and can implement complex policies (role checks, attribute-based access, hierarchical ownership). Use `IOwnerResolver` + `UseOwner()` when simple ownership-by-principal is sufficient. Use `IEntitlementProvider` when access logic is entity-specific or role-dependent.
