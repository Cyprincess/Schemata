# Mutation Pipeline

Every call to `AddAsync`, `UpdateAsync`, or `RemoveAsync` on `IRepository<TEntity>` runs a pipeline of advisors before (and sometimes instead of) the underlying store operation. Advisors are sorted by their `Order` property and executed sequentially. Each advisor returns an `AdviseResult`:

- **Continue** - proceed to the next advisor, then to the store operation.
- **Block** - stop the pipeline and skip the store operation entirely.
- **Handle** - stop the pipeline; the advisor has already performed an alternative action.

If any advisor returns `Block` or `Handle`, the remaining advisors are skipped and the backing-store call never executes.

## Where the code lives

| Item | Path |
|---|---|
| `IRepositoryAddAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryAddAdvisor.cs` |
| `IRepositoryUpdateAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryUpdateAdvisor.cs` |
| `IRepositoryRemoveAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryRemoveAdvisor.cs` |
| `IRepositoryCommittedAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryCommittedAdvisor.cs` |
| Built-in mutation advisors | `src/Schemata.Entity.Repository/Advisors/Advice{Add,Update,Remove}*.cs` |
| Registration | `src/Schemata.Entity.Repository/Extensions/ServiceCollectionExtensions.cs` |

## Advisor interfaces

Mutation advisor interfaces receive the repository and entity as arguments alongside the shared `AdviceContext`:

```csharp
public interface IRepositoryAddAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;

public interface IRepositoryUpdateAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;

public interface IRepositoryRemoveAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;
```

Committed advisors run after persistence succeeds and receive a commit snapshot:

```csharp
public interface IRepositoryCommittedAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, CommitChanges<TEntity>> where TEntity : class;
```

## Add pipeline

Built-in add advisors in execution order:

| Order | Advisor | Trait | Behavior |
|---|---|---|---|
| 100,000,000 | `AdviceAddTimestamp<TEntity>` | `ITimestamp` | Sets `CreateTime` and `UpdateTime` to `DateTime.UtcNow`. Suppressed by `TimestampSuppressed`. |
| 110,000,000 | `AdviceAddConcurrency<TEntity>` | `IConcurrency` | Mints a new `Guid` for `Timestamp`. Suppressed by `ConcurrencySuppressed`. |
| 220,000,000 | `AdviceAddCanonicalName<TEntity>` | `ICanonicalName` | Resolves the `[CanonicalName]` pattern and writes `CanonicalName`. No suppress flag. |
| 230,000,000 | `AdviceAddOwner<TEntity>` | `IOwnable` | Calls `IOwnerResolver<TEntity>.ResolveAsync` and sets `Owner`. Registered by `UseOwner()`. Suppressed by `OwnerSuppressed`. |
| 900,000,000 | `AdviceAddValidation<TEntity>` | (any) | Runs `IValidationAdvisor<TEntity>` for `Operations.Create`. Throws `ValidationException` on `Block`. Suppressed by `AddValidationSuppressed`. |
| 900,000,000 | `AdviceAddSoftDelete<TEntity>` | `ISoftDelete` | Clears `DeleteTime` to `null`. Suppressed by `SoftDeleteSuppressed`. |

After all advisors return `Continue`, the entity is added to the backing store's change tracker or pending operation list.

## Update pipeline

| Order | Advisor | Trait | Behavior |
|---|---|---|---|
| 100,000,000 | `AdviceUpdateTimestamp<TEntity>` | `ITimestamp` | Sets `UpdateTime` to `DateTime.UtcNow`. Suppressed by `TimestampSuppressed`. |
| 900,000,000 | `AdviceUpdateValidation<TEntity>` | (any) | Runs `IValidationAdvisor<TEntity>` for `Operations.Update`. Throws `ValidationException` on `Block`. Suppressed by `UpdateValidationSuppressed`. |
| 900,000,000 | `AdviceUpdateConcurrency<TEntity>` | `IConcurrency` | Loads the stored entity via `repository.GetAsync<IConcurrency>`, compares `Timestamp`, throws `ConcurrencyException` on mismatch, then mints a new `Guid`. Suppressed by `ConcurrencySuppressed`. |

After all advisors return `Continue`, the EF Core provider calls `Detach(entity)` then `Context.Update(entity)`. The detach clears any pre-existing tracker entry for the same key so `Context.Update` can attach the caller's instance without an "already tracked" conflict. See [providers.md](providers.md#detach-before-update).

`AdviceUpdateConcurrency` runs at `Orders.Max` (900,000,000) so it sees the final entity state after all other advisors have modified it.

## Remove pipeline

| Order | Advisor | Trait | Behavior |
|---|---|---|---|
| 900,000,000 | `AdviceRemoveSoftDelete<TEntity>` | `ISoftDelete` | Sets `DeleteTime = DateTime.UtcNow`, calls `repository.UpdateAsync(entity)`, returns `Handle` to prevent the physical delete. Suppressed by `SoftDeleteSuppressed`. |

When `AdviceRemoveSoftDelete` returns `Handle`, the entity stays in the database with a non-null `DeleteTime`. Subsequent queries exclude it automatically via `AdviceBuildQuerySoftDelete`. If the entity does not implement `ISoftDelete`, or if `SoftDeleteSuppressed` is active, the entity is physically removed.

## Committed pipeline

`IRepositoryCommittedAdvisor<TEntity>` runs after a standalone repository commit or unit-of-work commit succeeds. The cache package registers `AdviceCommittedEvictCache<TEntity>` at `Orders.Max`; it evicts reverse-indexed cache entries for updated and removed entities and honors `QueryCacheEvictionSuppressed`.

Committed advisors do not run when persistence fails or the unit of work rolls back.

## Registration

All built-in mutation advisors are registered automatically by `AddRepository`:

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>));
```

The DI container closes the open-generic registrations at resolve time, so `IRepositoryAddAdvisor<Book>` resolves `AdviceAddTimestamp<Book>`, `AdviceAddConcurrency<Book>`, and so on.

To add a custom advisor:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped(
    typeof(IRepositoryUpdateAdvisor<>),
    typeof(MyAuditAdvisor<>)));
```

Pick an `Order` outside the reserved range `[100_000_000, 900_000_000]` for user-defined advisors.

## Extension points

- **New mutation behavior**: implement the relevant advisor interface, check `entity is IMyTrait`, return `Continue` if the trait is absent.
- **Post-commit behavior**: implement `IRepositoryCommittedAdvisor<TEntity>` when the extension needs the final committed add/update/remove snapshot.
- **Suppression**: add a `sealed class MySuppressed;` marker, check `ctx.Has<MySuppressed>()` at the top of `AdviseAsync`, and expose a `SuppressMy()` method on a repository extension.

## See also

- [query-pipeline.md](query-pipeline.md) - build-query/query/result advisor chains
- [unit-of-work.md](unit-of-work.md) - explicit enlistment and committed advisors
- [entity/traits.md](../entity/traits.md) - trait interfaces and their advisor order numbers
- [core/advice-pipeline.md](../core/advice-pipeline.md) - `AdviseResult` semantics and runner mechanics
