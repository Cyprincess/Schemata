# Repository Ownership

The repository layer adds automatic owner assignment and owner-scoped query filtering. Entities implementing `IOwnable` are automatically stamped with the current principal's canonical name on create, and queries are automatically filtered to include only entities owned by that principal. The ownership contracts and advisors ship in `Schemata.Entity.Repository`.

Ownership is opt-in. Call `UseOwner()` on the repository builder to enable the advisors.

## Where the code lives

| Item                                              | Path                                                                                       |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `IOwnerResolver<TEntity>`                         | `src/Schemata.Entity.Repository/IOwnerResolver.cs`                                         |
| `SchemataOwnerOptions`                            | `src/Schemata.Entity.Repository/SchemataOwnerOptions.cs`                                   |
| `AdviceAddOwner<TEntity>`                         | `src/Schemata.Entity.Repository/Advisors/AdviceAddOwner.cs`                                |
| `AdviceBuildQueryOwner<TEntity>`                  | `src/Schemata.Entity.Repository/Advisors/AdviceBuildQueryOwner.cs`                         |
| `AdviceValidateResourceReferenceExistence<TEntity>` | `src/Schemata.Entity.Repository/Advisors/AdviceValidateResourceReferenceExistence.cs`    |
| `UseOwner` extension                              | `src/Schemata.Entity.Repository/Extensions/SchemataRepositoryBuilderOwnerExtensions.cs`    |

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

**Order:** `AdviceAddCanonicalName.DefaultOrder + 1_000_000` = 121,000,000. Runs after `AdviceAddCanonicalName` so the entity's own canonical name is settled before the owner is assigned.

**Behavior:**

1. If the entity does not implement `IOwnable`, skips.
2. If `IOwnable.Owner` is already non-null/non-empty, skips — callers can override the default by pre-setting the owner.
3. Calls `IOwnerResolver<TEntity>.ResolveAsync`. If the result is non-empty, assigns it to `IOwnable.Owner` and returns `Continue`.
4. When the resolver returns `null`, behavior is governed by `SchemataOwnerOptions.OnNullOwner`:
   - `Reject` (default) — throws `PermissionDeniedException`.
   - `EmptyResult` — returns `Block`.
   - `AllowAll` — returns `Continue`.

### AdviceBuildQueryOwner

`AdviceBuildQueryOwner<TEntity>` implements `IRepositoryBuildQueryAdvisor<TEntity>`. It applies a global query filter restricting results to entities owned by the current caller.

**Order:** `AdviceBuildQuerySoftDelete.DefaultOrder + 10_000_000` = 110,000,000.

**Behavior:**

1. If the query passes `QueryOptions.IgnoreOwner`, skips.
2. If `TEntity` does not implement `IOwnable`, skips.
3. Calls `IOwnerResolver<TEntity>.ResolveAsync`. If non-empty, appends `.OfType<IOwnable>().Where(e => e.Owner == owner).OfType<TEntity>()`.
4. When the resolver returns `null`, applies `SchemataOwnerOptions.OnNullOwner` (same policy as `AdviceAddOwner`).

### AdviceValidateResourceReferenceExistence

`AdviceValidateResourceReferenceExistence<TEntity>` belongs to Repository and is registered by
`AddRepository` for add/update operations. Each property opts in with `ValidateExistence = true`.
Its target query passes `QueryOptions(IgnoreOwner: true)`; ownership installation is independent.
The validation creates no database foreign keys or cascades and
cannot prevent a target from being deleted after the check.

**Order:** `AdviceValidateResourceReferences.DefaultOrder + 10_000_000` = 150,000,000, on both the add
and update pipelines. Type resolvability is established before any existence query executes.

**Behavior:**

1. Collects the entity's string properties carrying `[ResourceReference(ValidateExistence = true)]`. An
   entity with none is skipped in constant time.
2. Resolves the target type: the attribute's `Target` when set, otherwise
   `IResourceTypeResolver.Resolve(value)` for polymorphic references. Targets that do not implement
   `ICanonicalName` are skipped; an unregistered `IResourceTypeResolver` throws
   `InvalidOperationException`.
3. Queries the target's `IRepository<TTarget>` for any row with the matching `CanonicalName`, with
   `QueryOptions(IgnoreOwner: true)` so cross-owner references resolve.
4. A missing row throws `NotFoundException` (`SchemataResourceErrors.NotFound(target, value)`).

## Registration

```csharp
services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
        .UseOwner();
```

`UseOwner()` registers `SchemataOwnerOptions`, `AdviceBuildQueryOwner<>` and `AdviceAddOwner<>`. Registrations use `TryAddEnumerable`; logical reference existence validation is registered independently by Repository.

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

## Per-operation owner options

Ownership is controlled per call. A query that must include every owner passes
`QueryOptions(IgnoreOwner: true)`, and `AdviceBuildQueryOwner` skips the filter for that call only.
Insert stamping has no bypass: `AdviceAddOwner` assigns the owner only while `Owner` is unset, so
pre-assign the value the seed or import needs.

```csharp
var all = await repository.ListAsync<Document>(
    null, ct, new QueryOptions(IgnoreOwner: true)).ToListAsync(ct);
```

The option travels with the call, so the resource handler's own `IRepository<TEntity>` follows the
same rule. A consumer that needs owner-free reads of its own resource passes the option at that
call site; a request advisor cannot affect the handler's repository because the two instances are
separate.

## Override and bypass

`AdviceAddOwner` leaves an already-set `Owner` untouched, so a caller that pre-assigns ownership
keeps control. The advisor fires whether the entity reaches the repository through the resource
layer or directly.

## Extension points

- **Custom `OnNullOwner` policy**: configure `SchemataOwnerOptions.OnNullOwner` to `AllowAll` for background jobs that run without a principal.
- **Per-entity resolver**: register `IOwnerResolver<MyEntity>` with a closed-generic registration to override the open-generic fallback for a specific entity type.

## Design motivation

Ownership filtering is simple equality (`Owner == resolvedOwner`). It requires no custom LINQ expression and no per-entity provider. For complex access policies (role checks, attribute-based access, hierarchical ownership), use `IEntitlementProvider` from `Schemata.Security.Skeleton` instead.

## See also

- [entity/traits.md](../entity/traits.md) — `IOwnable` interface definition
- [query-pipeline.md](query-pipeline.md) — `AdviceBuildQueryOwner` in the build-query stage
- [mutation-pipeline.md](mutation-pipeline.md) — `AdviceAddOwner` in the add pipeline
