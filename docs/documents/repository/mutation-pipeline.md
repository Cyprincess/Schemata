# Mutation Pipeline

Every call to `AddAsync`, `UpdateAsync`, or `RemoveAsync` on `IRepository<TEntity>` runs a pipeline of advisors before (and sometimes instead of) the underlying store operation. Advisors are sorted by their `Order` property and executed sequentially. Each advisor returns an `AdviseResult`:

- **Continue** -- proceed to the next advisor, then to the store operation.
- **Block** -- stop the pipeline and skip the store operation entirely.
- **Handle** -- stop the pipeline; the advisor has already performed an alternative action (e.g. soft-delete converts a remove into an update).

If any advisor returns `Block` or `Handle`, the remaining advisors are skipped and the backing-store call (EF Core `Add`, `Update`, or `Remove`) never executes.

## Advisor Interfaces

Each mutation has its own advisor interface. All three extend `IAdvisor<IRepository<TEntity>, TEntity>`, receiving the repository and entity as arguments alongside the shared `AdviceContext`.

```csharp
public interface IRepositoryAddAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;

public interface IRepositoryUpdateAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;

public interface IRepositoryRemoveAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;
```

The pipeline runner resolves all registered implementations of the relevant interface from the DI container, sorts them by `Order`, and calls `AdviseAsync` on each one.

## Add Pipeline

The built-in add advisors execute in this order:

| Order       | Advisor                           | Trait            | Behavior                                                                                                                                                            |
| ----------- | --------------------------------- | ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 100,000,000 | `AdviceAddTimestamp<TEntity>`     | `ITimestamp`     | Sets `CreateTime` and `UpdateTime` to `DateTime.UtcNow`. Suppressed by `SuppressTimestamp`.                                                                         |
| 110,000,000 | `AdviceAddConcurrency<TEntity>`   | `IConcurrency`   | Generates a new `Guid` for the `Timestamp` property. Suppressed by `SuppressConcurrency`.                                                                           |
| 120,000,000 | `AdviceAddCanonicalName<TEntity>` | `ICanonicalName` | Resolves the entity's resource-name pattern and sets `CanonicalName`. Not suppressible.                                                                             |
| 130,000,000 | `AdviceAddValidation<TEntity>`    | (any)            | Runs all registered `IValidationAdvisor<TEntity>` advisors for `Operations.Create`. Throws `ValidationException` on `Block`. Suppressed by `SuppressAddValidation`. |
| 130,000,000 | `AdviceAddOwner<TEntity>`         | `IOwnable`       | Resolves the owner via `IOwnerResolver<TEntity>` and assigns it to `IOwnable.Owner`. Registered by `UseOwner()`. Suppressed by `SuppressOwner`.                    |
| 900,000,000 | `AdviceAddSoftDelete<TEntity>`    | `ISoftDelete`    | Clears `DeleteTime` to `null`, ensuring newly added entities are never marked as deleted. Suppressed by `SuppressSoftDelete`.                                       |

After all advisors return `Continue`, the entity is added to the backing store's change tracker (e.g. `DbContext.AddAsync`).

## Update Pipeline

| Order       | Advisor                            | Trait          | Behavior                                                                                                                                                                                                                                                     |
| ----------- | ---------------------------------- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 100,000,000 | `AdviceUpdateTimestamp<TEntity>`   | `ITimestamp`   | Sets `UpdateTime` to `DateTime.UtcNow`. Suppressed by `SuppressTimestamp`.                                                                                                                                                                                   |
| 110,000,000 | `AdviceUpdateValidation<TEntity>`  | (any)          | Runs all registered `IValidationAdvisor<TEntity>` advisors for `Operations.Update`. Throws `ValidationException` on `Block`. Suppressed by `SuppressUpdateValidation`.                                                                                       |
| 900,000,000 | `AdviceUpdateConcurrency<TEntity>` | `IConcurrency` | Loads the stored entity via `repository.GetAsync<IConcurrency>` and compares its `Timestamp` to the incoming entity's `Timestamp`. Throws `ConcurrencyException` on mismatch. On success, generates a new `Guid` stamp. Suppressed by `SuppressConcurrency`. |
| 900,000,000 | `AdviceUpdateEvictCache<TEntity>`   | (any)          | Evicts cached query results for this entity via the reverse index. Registered by `UseQueryCache()`. Suppressed by `SuppressQueryCacheEviction`.                                                                                                              |

After all advisors return `Continue`, the entity is detached (to avoid "already tracked" errors) and then marked as updated in the change tracker.

Concurrency checking and cache eviction both run at `Orders.Max` intentionally: concurrency needs to see the final state after all other advisors have modified the entity, and its `GetAsync` call must reflect the current database state. Cache eviction must run after concurrency succeeds — evicted data is re-cached on the next query regardless of whether the update ultimately commits.

## Remove Pipeline

| Order       | Advisor                           | Trait         | Behavior                                                                                                                                                                                                       |
| ----------- | --------------------------------- | ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 900,000,000 | `AdviceRemoveSoftDelete<TEntity>` | `ISoftDelete` | Sets `DeleteTime` to `DateTime.UtcNow` and calls `repository.UpdateAsync` to persist the soft-delete. Returns `Handle`, which prevents the physical delete from occurring. Suppressed by `SuppressSoftDelete`. |
| 900,000,000 | `AdviceRemoveEvictCache<TEntity>` | (any)          | Evicts cached query results for this entity via the reverse index. Registered by `UseQueryCache()`. Suppressed by `SuppressQueryCacheEviction`.                                                                 |

When `AdviceRemoveSoftDelete` handles the remove, the entity stays in the database with a non-null `DeleteTime`. Subsequent queries automatically exclude it via the build-query soft-delete filter.

If the entity does not implement `ISoftDelete`, or if `SuppressSoftDelete` is active, no advisor intervenes and the entity is physically removed from the store.

## Pipeline Execution Order

Advisors are sorted by their integer `Order` property (ascending). The order constants are defined as a chain rooted at `SchemataConstants.Orders.Base` (100,000,000), with each subsequent advisor offset by 10,000,000. The terminal anchor `SchemataConstants.Orders.Max` (900,000,000) is reserved for advisors that must run last.

Custom advisors can slot in at any point by choosing an appropriate `Order` value. For example, an advisor with `Order = 115_000_000` would run between `AdviceAddConcurrency` (110,000,000) and `AdviceAddCanonicalName` (120,000,000) in the add pipeline.

## Registration

All built-in advisors are registered automatically when you call `AddRepository` on `IServiceCollection`:

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>));
```

This registers open-generic implementations for every built-in advisor interface. The DI container closes the generic at resolve time, so `IRepositoryAddAdvisor<Product>` resolves `AdviceAddTimestamp<Product>`, `AdviceAddConcurrency<Product>`, and so on.

To add a custom advisor, register it as an additional enumerable entry:

```csharp
services.AddScoped(
    typeof(IRepositoryAddAdvisor<>),
    typeof(MyCustomAddAdvisor<>));
```
