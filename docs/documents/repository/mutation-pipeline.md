# Mutation Pipeline

Every `AddAsync`, `UpdateAsync`, and `RemoveAsync` on `IRepository<TEntity>` runs an advisor pipeline
before — and sometimes instead of — the backing-store operation. Advisors are sorted by their `Order`
property and run in sequence. Each returns an `AdviseResult`:

- **Continue** — proceed to the next advisor, then to the store operation.
- **Block** — stop the pipeline; skip the store operation.
- **Handle** — stop the pipeline; the advisor has performed an alternative action in place of the store
  operation.

`Block` or `Handle` skips the remaining advisors and the backing-store call.

## Where the code lives

| Item | Path |
| --- | --- |
| `IRepositoryAddAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryAddAdvisor.cs` |
| `IRepositoryUpdateAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryUpdateAdvisor.cs` |
| `IRepositoryRemoveAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryRemoveAdvisor.cs` |
| `IRepositoryCommittedAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryCommittedAdvisor.cs` |
| Built-in advisors | `src/Schemata.Entity.Repository/Advisors/Advice{Add,Update,Remove}*.cs` |
| Registration | `src/Schemata.Entity.Repository/Extensions/ServiceCollectionExtensions.cs` |

## Advisor interfaces

The add, update, and remove advisor interfaces receive the repository and entity alongside the shared
`AdviceContext`:

```csharp
public interface IRepositoryAddAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;

public interface IRepositoryUpdateAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;

public interface IRepositoryRemoveAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;
```

Committed advisors run after persistence succeeds and receive a change snapshot:

```csharp
public interface IRepositoryCommittedAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, CommitChanges<TEntity>> where TEntity : class;
```

## Add pipeline

Built-in add advisors, in execution order:

| Order | Advisor | Trait | Behavior |
| --- | --- | --- | --- |
| 100,000,000 | `AdviceAddTimestamp<TEntity>` | `ITimestamp` | Sets `CreateTime` and `UpdateTime` to the current UTC time. Suppressed by `TimestampSuppressed`. |
| 110,000,000 | `AdviceAddConcurrency<TEntity>` | `IConcurrency` | Mints a new GUID for `Timestamp`. |
| 220,000,000 | `AdviceAddCanonicalName<TEntity>` | `ICanonicalName` | Resolves the `[CanonicalName]` pattern and writes `CanonicalName`. No suppress flag. |
| 230,000,000 | `AdviceAddOwner<TEntity>` | `IOwnable` | Calls `IOwnerResolver<TEntity>.ResolveAsync` and sets `Owner`. Registered by `UseOwner()`. Suppressed by `OwnerSuppressed`. |
| 230,000,000 | `AdviceAddValidation<TEntity>` | (any) | Runs `IValidationAdvisor<TEntity>` for `Operations.Create`. Throws `ValidationException` when an advisor blocks. Suppressed by `AddValidationSuppressed`. |
| 240,000,000 | `AdviceAddUniqueness<TEntity>` | (any) | Looks up the entity by key (with the query soft-delete filter suppressed); throws `AlreadyExistsException` when a row already exists. Suppressed by `UniquenessSuppressed`. |
| 900,000,000 | `AdviceAddSoftDelete<TEntity>` | `ISoftDelete` | Clears `DeleteTime` to `null`. Suppressed by `SoftDeleteSuppressed`. |

After every advisor returns `Continue`, the entity is staged for the store: EF Core calls
`Context.AddAsync(entity)`; LinqToDB inserts immediately inside the active transaction.

`AdviceAddOwner` is in scope only when `UseOwner()` has registered it; it shares order 230,000,000 with
`AdviceAddValidation`. `AdviceAddUniqueness` is optimistic — a concurrent insert between its lookup and
the commit still surfaces as the provider's own constraint error.

## Update pipeline

| Order | Advisor | Trait | Behavior |
| --- | --- | --- | --- |
| 100,000,000 | `AdviceUpdateTimestamp<TEntity>` | `ITimestamp` | Sets `UpdateTime` to the current UTC time. Suppressed by `TimestampSuppressed`. |
| 110,000,000 | `AdviceUpdateValidation<TEntity>` | (any) | Runs `IValidationAdvisor<TEntity>` for `Operations.Update`. Throws `ValidationException` when an advisor blocks. Suppressed by `UpdateValidationSuppressed`. |

There is no update-side concurrency advisor. Optimistic concurrency on update is enforced by the
database when the concrete entity annotates `IConcurrency.Timestamp` with `[ConcurrencyCheck]`. EF Core
detaches the entity, re-attaches it as modified, and bumps the current `Timestamp` so `SaveChangesAsync`
issues a guarded `UPDATE ... WHERE Timestamp = @original`; a zero-row result becomes
`AbortedException`. LinqToDB calls `UpdateOptimisticAsync` for the same effect. See
[providers.md](providers.md) and [entity/traits.md](../entity/traits.md#iconcurrency).

## Remove pipeline

| Order | Advisor | Trait | Behavior |
| --- | --- | --- | --- |
| 900,000,000 | `AdviceRemoveSoftDelete<TEntity>` | `ISoftDelete` | Sets `DeleteTime` to the current UTC time, calls `repository.UpdateAsync(entity)`, and returns `Handle` to prevent the physical delete. Suppressed by `SoftDeleteSuppressed`. |

When `AdviceRemoveSoftDelete` returns `Handle`, the row stays with a non-null `DeleteTime`, and later
queries exclude it via `AdviceBuildQuerySoftDelete`. When the entity does not implement `ISoftDelete`,
or `SoftDeleteSuppressed` is active, the entity is physically removed.

## Committed pipeline

`IRepositoryCommittedAdvisor<TEntity>` runs after a standalone repository commit or a unit-of-work
commit succeeds, receiving the `CommitChanges<TEntity>` snapshot of added, updated, and removed
entities. The cache package registers `AdviceCommittedEvictCache<TEntity>` at order 900,000,000; it
evicts reverse-indexed cache entries for updated and removed entities and honors
`QueryCacheEvictionSuppressed`. Committed advisors do not run when persistence fails or the unit of work
rolls back.

## Registration

`AddRepository` registers all built-in mutation advisors as open generics:

```csharp
services.AddRepository(typeof(EfCoreRepository<,>));
```

The container closes each open generic at resolve time, so `IRepositoryAddAdvisor<Book>` materializes
`AdviceAddTimestamp<Book>`, `AdviceAddConcurrency<Book>`, and the rest. Add a custom advisor with
`TryAddEnumerable`:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped(
    typeof(IRepositoryUpdateAdvisor<>),
    typeof(MyAuditAdvisor<>)));
```

Pick an `Order` outside the built-in `[100_000_000, 900_000_000]` window.

## Extension points

- **New mutation behavior** — implement the relevant advisor interface, check `entity is IMyTrait`, and
  return `Continue` when the trait is absent.
- **Post-commit behavior** — implement `IRepositoryCommittedAdvisor<TEntity>` when the extension needs
  the final add/update/remove snapshot.
- **Suppression** — add a `sealed class MySuppressed;` marker, check `ctx.Has<MySuppressed>()` at the
  top of `AdviseAsync`, and expose a `SuppressMy()` extension that calls `AdviceContext.Use<MySuppressed>()`.

## See also

- [query-pipeline.md](query-pipeline.md) — build-query/query/result advisor chains
- [unit-of-work.md](unit-of-work.md) — enlistment and committed advisors
- [entity/traits.md](../entity/traits.md) — trait interfaces and advisor order numbers
