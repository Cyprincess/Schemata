# Unit of Work

The unit-of-work pattern coordinates multiple repository mutations within a single database transaction. `IUnitOfWork` provides explicit begin/commit/rollback control, while the repository layer automatically detects an active unit of work and delegates commit responsibility to it.

## Where the code lives

| Item | Path |
|---|---|
| `IUnitOfWork` | `src/Schemata.Entity.Repository/IUnitOfWork.cs` |
| `IUnitOfWork<TContext>` | `src/Schemata.Entity.Repository/IUnitOfWork.cs` |
| `RepositoryBase.EnqueueAfterCommit` | `src/Schemata.Entity.Repository/RepositoryBase.cs` |
| `RepositoryBase.BeginWork` | `src/Schemata.Entity.Repository/RepositoryBase.cs` |

## IUnitOfWork interface

```csharp
public interface IUnitOfWork : IDisposable
{
    bool IsActive { get; }
    void Begin();
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
    void EnqueueAfterCommit(Func<CancellationToken, Task> action);
}

public interface IUnitOfWork<TContext> : IUnitOfWork;
```

| Member | Purpose |
|---|---|
| `IsActive` | `true` when a transaction has been started and not yet committed or rolled back. |
| `Begin()` | Starts a new database transaction. |
| `CommitAsync` | Persists changes, commits the transaction, then drains the after-commit queue. |
| `RollbackAsync` | Rolls back without persisting changes. Discards the after-commit queue. |
| `EnqueueAfterCommit` | Adds an action to run after `CommitAsync` succeeds. |
| `Dispose()` | Rolls back if still active; discards the after-commit queue. |

`IUnitOfWork<TContext>` carries a type parameter bound to the concrete data context (`DbContext` or `DataConnection`), enabling multiple provider types to coexist in the same DI container.

## How repositories interact with a unit of work

Every `RepositoryBase<TEntity>` constructor accepts an optional `IUnitOfWork?` parameter. When a unit of work is injected:

1. `CommitAsync` on the repository checks `UnitOfWork?.IsActive`. If active, it throws `InvalidOperationException` — commit must happen via `uow.CommitAsync()`.
2. `EnqueueAfterCommit` on the repository routes the action to `UnitOfWork.EnqueueAfterCommit` when the unit of work is active, so a single transaction-wide drain happens at `uow.CommitAsync`. Without an active unit of work, the repository drains its own queue after `CommitAsync`.
3. `BeginWork()` calls `UnitOfWork.Begin()` and returns the unit of work. Calling `BeginWork()` on a repository that has no registered `IUnitOfWork<TContext>` throws `InvalidOperationException`.

## BeginWork and transaction scope

```csharp
using var uow = orders.BeginWork();
await orders.AddAsync(order);
await items.AddAsync(lineItem);
await uow.CommitAsync();  // one transaction, one round-trip
```

When repositories share the same scoped `DbContext`, a call to `BeginWork()` on any of them creates a transaction that covers all tracked changes — including those made by sibling repositories.

```csharp
public class OrderService(
    IRepository<Order> orders,
    IRepository<OrderItem> items)
{
    public async Task PlaceAsync(Order order, List<OrderItem> lines)
    {
        using var uow = orders.BeginWork();
        await orders.AddAsync(order);
        foreach (var line in lines)
            await items.AddAsync(line);
        await uow.CommitAsync();
    }
}
```

`IUnitOfWork<TContext>` is registered as scoped, so all injections within the same HTTP request resolve the same instance. The `IsActive` flag prevents nested transactions.

## EnqueueAfterCommit and the after-commit queue

`EnqueueAfterCommit(Func<CancellationToken, Task> action)` queues work that must observe a successful persistence boundary before running. Cache eviction advisors use this to avoid evicting entries that might be repopulated with stale data if the transaction later rolls back.

```csharp
repository.EnqueueAfterCommit(async ct => {
    await cache.RemoveAsync(key, ct);
});
await repository.CommitAsync();  // eviction runs here, after SaveChangesAsync succeeds
```

The queue lives on both `RepositoryBase` and `IUnitOfWork`. When a unit of work is active, `EnqueueAfterCommit` on the repository routes to the unit of work's queue, so the drain happens once at `uow.CommitAsync`. On rollback or dispose, the queue is discarded — never put critical persistence in the after-commit queue.

`DrainAfterCommitAsync` (called internally by `CommitAsync`) runs all queued actions sequentially, collects exceptions, and rethrows as `AggregateException` if more than one action failed.

## Once() and SuppressX() state markers

`Once()` creates a new repository instance with a fresh `AdviceContext` via `ActivatorUtilities.CreateInstance(ServiceProvider, GetType())`. The new instance shares the same underlying provider context but has its own suppression state.

```csharp
// Side query that must see soft-deleted rows without polluting the request-scoped state
var tombstone = await _repository.Once()
    .SuppressQuerySoftDelete()
    .FirstOrDefaultAsync<Book>(q => q.Where(b => b.Name == name));
```

Every `SuppressX()` method stores a state-noun marker class in `AdviceContext` and returns `this`. The naming convention: the method is a verb (`SuppressSoftDelete()`); the marker is a state noun (`SoftDeleteSuppressed`). Advisors check `ctx.Has<SoftDeleteSuppressed>()` at the top of `AdviseAsync`.

## Registration

Chain `.WithUnitOfWork<TContext>()` on the repository builder:

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .WithUnitOfWork<AppDbContext>();
```

`.WithUnitOfWork<TContext>()` registers `IUnitOfWork<TContext>` as scoped using `TryAdd`, so a prior registration takes precedence.

## Scoped repository sharing without explicit UoW

Even without an explicit `IUnitOfWork`, repositories sharing the same scoped `DbContext` naturally batch operations. A single `CommitAsync` on any repository flushes all pending changes to that context:

```csharp
await students.AddAsync(student);
await courses.AddAsync(course);
await students.CommitAsync();  // flushes both student and course
```

## Caveats

- Calling `repository.CommitAsync()` while a unit of work is active throws `InvalidOperationException`. Always commit via `uow.CommitAsync()` when a unit of work is in scope.
- The after-commit queue is discarded on rollback or dispose. Do not enqueue critical persistence work there.
- `BeginWork()` requires `IUnitOfWork<TContext>` to be registered. Calling it without registration throws `InvalidOperationException` with a message directing you to call `.WithUnitOfWork<TContext>()`.

## See also

- [overview.md](overview.md) — repository API and `Once()` / `Suppress*()` reference
- [mutation-pipeline.md](mutation-pipeline.md) — how advisors use `EnqueueAfterCommit`
- [caching.md](caching.md) — cache eviction via the after-commit queue
- [providers.md](providers.md) — EF Core and LinqToDB UoW implementations
