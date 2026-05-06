# Overview

`IRepository<TEntity>` is the primary data-access abstraction in Schemata. It wraps a backing store (Entity Framework Core, LinqToDB, or any future provider) behind a uniform API and routes every read and write through an advisor pipeline that handles timestamps, concurrency stamps, soft-delete, validation, and canonical-name generation automatically.

A non-generic `IRepository` mirror exists for type-erased scenarios (the Resource layer uses it to operate on entities whose type is known only at runtime). Both interfaces share the same underlying implementation -- `RepositoryBase<TEntity>` implements both and delegates the non-generic calls to the generic ones after a type check.

## Public API

### Queries

```csharp
IAsyncEnumerable<TEntity> AsAsyncEnumerable();
IQueryable<TEntity>       AsQueryable();

IAsyncEnumerable<TResult> ListAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
    CancellationToken ct = default);

IAsyncEnumerable<TResult> SearchAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
    CancellationToken ct = default);

ValueTask<TEntity?> GetAsync(TEntity entity, CancellationToken ct = default);
ValueTask<TResult?> GetAsync<TResult>(TEntity entity, CancellationToken ct = default);

ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default);
ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default);

ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
    CancellationToken ct = default);

ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
    CancellationToken ct = default);

ValueTask<bool> AnyAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
    CancellationToken ct = default);

ValueTask<int>  CountAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
    CancellationToken ct = default);

ValueTask<long> LongCountAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
    CancellationToken ct = default);
```

`AsAsyncEnumerable` and `AsQueryable` return the raw data set **without** running the advisor pipeline. Every other query method applies build-query advisors (for example, the soft-delete filter) before executing.

The `predicate` parameter is a `Func<IQueryable<TEntity>, IQueryable<TResult>>` -- a query transformation, not a simple expression tree. This lets you chain `Where`, `Select`, `OrderBy`, `Take`, and any other LINQ operator in a single lambda:

```csharp
var page = repository.ListAsync<ProductDto>(q =>
    q.Where(p => p.Price > 10)
     .OrderBy(p => p.Name)
     .Select(p => new ProductDto(p.Id, p.Name))
     .Skip(20)
     .Take(10));
```

`GetAsync` extracts key values from an entity instance (via `[TableKey]` attributes or the `Id` convention) and delegates to `FindAsync`. `FindAsync` builds a dynamic key-equality predicate and calls `SingleOrDefaultAsync`.

### Mutations

```csharp
Task AddAsync(TEntity entity, CancellationToken ct = default);
Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

Task UpdateAsync(TEntity entity, CancellationToken ct = default);

Task RemoveAsync(TEntity entity, CancellationToken ct = default);
Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
```

Each mutation runs the corresponding advisor pipeline (`IRepositoryAddAdvisor`, `IRepositoryUpdateAdvisor`, or `IRepositoryRemoveAdvisor`) before touching the backing store. If any advisor returns `Block` or `Handle`, the mutation short-circuits and the store operation is skipped.

`AddRangeAsync` and `RemoveRangeAsync` fan out to individual `AddAsync` / `RemoveAsync` calls so that every entity gets its own full pass through the pipeline.

### Commit

```csharp
ValueTask<int> CommitAsync(CancellationToken ct = default);
```

The repository uses a unit-of-work pattern. `AddAsync`, `UpdateAsync`, and `RemoveAsync` stage changes in the provider's change tracker but do not persist them. You must call `CommitAsync` to flush all pending changes to the database in a single transaction. The return value is the number of rows affected.

```csharp
await repository.AddAsync(entity);
await repository.UpdateAsync(other);
var rows = await repository.CommitAsync();
```

### Detach

```csharp
void Detach(TEntity entity);
```

Removes the entity from the change tracker. The EF Core implementation sets `EntityState.Detached`. This is called internally before `Update` to avoid "already tracked" conflicts.

## Once

```csharp
IRepository<TEntity> Once();
```

`Once` creates a **new repository instance** with a fresh `AdviceContext`. Suppression flags set on the new instance do not affect the original, and vice versa. This is essential when you need to bypass a behavior for a single operation without polluting the shared instance:

```csharp
// Permanently suppress on this instance -- every subsequent call skips timestamps
repository.SuppressTimestamp();
await repository.AddAsync(entity1);
await repository.AddAsync(entity2);  // also skips timestamps

// Suppress for one call only
await repository.Once().SuppressTimestamp().AddAsync(entity3);
await repository.AddAsync(entity4);  // timestamps applied normally
```

Under the hood, `Once` calls `ActivatorUtilities.CreateInstance` to construct a sibling instance of the same concrete type. The new instance shares the same `DbContext` (or equivalent provider context) but has its own `AdviceContext`.

## Suppression Methods

Every suppression method stores a marker type in the `AdviceContext`. The corresponding advisor checks for that marker at the start of its `AdviseAsync` and skips its logic when the marker is present. All methods return the repository instance for fluent chaining.

| Method                          | Marker                        | Advisors Affected                                 |
| ------------------------------- | ----------------------------- | ------------------------------------------------- |
| `SuppressAddValidation()`       | `SuppressAddValidation`       | `AdviceAddValidation`                             |
| `SuppressUpdateValidation()`    | `SuppressUpdateValidation`    | `AdviceUpdateValidation`                          |
| `SuppressConcurrency()`         | `SuppressConcurrency`         | `AdviceAddConcurrency`, `AdviceUpdateConcurrency` |
| `SuppressQuerySoftDelete()`     | `SuppressQuerySoftDelete`     | `AdviceBuildQuerySoftDelete`                      |
| `SuppressSoftDelete()`          | `SuppressSoftDelete`          | `AdviceAddSoftDelete`, `AdviceRemoveSoftDelete`   |
| `SuppressTimestamp()`           | `SuppressTimestamp`           | `AdviceAddTimestamp`, `AdviceUpdateTimestamp`     |
| `SuppressOwner()`               | `OwnerSuppressed`             | `AdviceAddOwner`                                  |
| `SuppressQueryOwner()`          | `QueryOwnerSuppressed`        | `AdviceBuildQueryOwner`                           |
| `SuppressQueryCache()`          | `QueryCacheSuppressed`        | `AdviceQueryCache`, `AdviceResultCache`           |
| `SuppressQueryCacheEviction()`  | `QueryCacheEvictionSuppressed` | `AdviceUpdateEvictCache`, `AdviceRemoveEvictCache` |

The owner and cache suppression methods are available only when the corresponding packages (`Schemata.Entity.Owner`, `Schemata.Entity.Cache`) are referenced and their `UseOwner()` / `UseQueryCache()` methods have been called.

## AdviceContext

Every repository instance holds an `AdviceContext` property. This is a typed property bag that flows through every advisor call. Beyond the suppression markers, advisors and application code can store arbitrary state in it via `Set<T>`, `TryGet<T>`, `Get<T>`, and `Has<T>`.

```csharp
repository.AdviceContext.Set<MyCustomFlag>(new MyCustomFlag());
```

The `AdviceContext` also carries the `IServiceProvider`, giving advisors access to any DI-registered service during pipeline execution.

## Unit of Work

`IRepository<TEntity>` is registered as **scoped**. Within a single DI scope (typically one HTTP request), all injections of `IRepository<Product>` resolve to the same instance backed by the same `DbContext`. This means multiple repositories share the same underlying context:

```csharp
public class OrderService(
    IRepository<Order> orders,
    IRepository<OrderItem> items)
{
    public async Task PlaceAsync(Order order, List<OrderItem> lines)
    {
        await orders.AddAsync(order);
        foreach (var line in lines) {
            await items.AddAsync(line);
        }
        // One round-trip, one transaction
        await orders.CommitAsync();
    }
}
```

Because both repositories share the same `DbContext`, a single `CommitAsync` on either repository flushes all pending changes.

For explicit transaction control, use `IUnitOfWork<TContext>`. Call `repository.BeginWork()` to start a transaction that coordinates all mutations until `CommitAsync` or `RollbackAsync`:

```csharp
using var uow = orders.BeginWork();
await orders.AddAsync(order);
await items.AddAsync(lineItem);
await uow.CommitAsync();
```

`IUnitOfWork<TContext>` is registered when you chain `.WithUnitOfWork<TContext>()` on the repository builder. See [Unit of Work](unit-of-work.md) for the full API and provider-specific behavior.
