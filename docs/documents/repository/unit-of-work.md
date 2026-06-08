# Unit of Work

The unit-of-work pattern coordinates multiple repository mutations within a single database transaction. `IUnitOfWork` provides explicit begin/commit/rollback control, and repositories opt in through `Enlist`.

## Where the code lives

| Item | Path |
|---|---|
| `IUnitOfWork` | `src/Schemata.Entity.Repository/IUnitOfWork.cs` |
| `IUnitOfWork<TContext>` | `src/Schemata.Entity.Repository/IUnitOfWork.cs` |
| `IRepository.Enlist` | `src/Schemata.Entity.Repository/IRepository.cs` |
| `IRepositoryCommittedAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryCommittedAdvisor.cs` |
| `CommitChanges<TEntity>` | `src/Schemata.Entity.Repository/CommitChanges.cs` |

## IUnitOfWork interface

```csharp
public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    bool IsActive { get; }
    void Begin();
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

public interface IUnitOfWork<TContext> : IUnitOfWork
{
    TContext Context { get; }
}
```

| Member | Purpose |
|---|---|
| `IsActive` | `true` when a transaction has been started and not yet committed or rolled back. |
| `Begin()` | Starts a new database transaction and owns a fresh data context. |
| `CommitAsync` | Persists enlisted repository changes, commits the transaction, then dispatches committed advisors. |
| `RollbackAsync` | Rolls back the transaction and clears enlisted repository tracking state. |
| `Dispose()` / `DisposeAsync()` | Rolls back if still active and releases the owned data context. |

`IUnitOfWork<TContext>` carries a type parameter bound to the concrete data context (`DbContext` or `DataConnection`), enabling multiple provider types to coexist in the same DI container.

## Repository enlistment

Repositories are transient. Each resolution owns its own provider context, so repositories from the same DI scope can query and write independently. To share a transaction, begin a unit of work and enlist each repository:

```csharp
uow.Begin();
orders.Enlist(uow);
items.Enlist(uow);

await orders.AddAsync(order);
await items.AddAsync(lineItem);

await uow.CommitAsync();
```

After enlistment, the repository adopts the unit of work's context and registers commit/rollback sinks with the provider implementation. Calling `repository.CommitAsync()` while enlisted throws `InvalidOperationException`; commit through `uow.CommitAsync()`.

```csharp
public class OrderService(
    IRepository<Order>          orders,
    IRepository<OrderItem>      items,
    IUnitOfWork<AppDbContext>   uow)
{
    public async Task PlaceAsync(Order order, List<OrderItem> lines)
    {
        uow.Begin();
        orders.Enlist(uow);
        items.Enlist(uow);

        await orders.AddAsync(order);
        foreach (var line in lines)
            await items.AddAsync(line);

        await uow.CommitAsync();
    }
}
```

`IUnitOfWork<TContext>` is registered as scoped, so all injections within the same HTTP request resolve the same instance. The current implementation is single-use per instance: begin once, commit or roll back, then dispose.

## Committed advisor pipeline

`IRepositoryCommittedAdvisor<TEntity>` runs after a successful commit boundary. It receives a typed snapshot of entity changes:

```csharp
public sealed class CommitChanges<TEntity>
    where TEntity : class
{
    public IReadOnlyList<TEntity> Added { get; init; }
    public IReadOnlyList<TEntity> Updated { get; init; }
    public IReadOnlyList<TEntity> Removed { get; init; }
}
```

Standalone `repository.CommitAsync()` snapshots the repository's tracked adds, updates, and removes before persisting, then dispatches committed advisors after persistence succeeds. `uow.CommitAsync()` invokes the committed sinks registered by every enlisted repository after the transaction commits.

Query cache eviction uses this pipeline: updated and removed entities evict reverse-indexed entries after commit; added entities do not evict.

## Once() and SuppressX() state markers

`Once()` creates a new repository instance with a fresh `AdviceContext` via `ActivatorUtilities.CreateInstance(ServiceProvider, GetType())`.

```csharp
var tombstone = await _repository.Once()
    .SuppressQuerySoftDelete()
    .FirstOrDefaultAsync<Book>(q => q.Where(b => b.Name == name));
```

Every `SuppressX()` method stores a state-noun marker class in `AdviceContext` and returns `this`. Advisors check `ctx.Has<SoftDeleteSuppressed>()` at the top of `AdviseAsync`.

## Registration

Chain `.WithUnitOfWork<TContext>()` on the repository builder:

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .WithUnitOfWork<AppDbContext>();
```

`.WithUnitOfWork<TContext>()` registers `IUnitOfWork<TContext>` as scoped using `TryAdd`, so a prior registration takes precedence.

## Independent repositories without UoW

Without explicit enlistment, transient repositories do not share a provider context. Commit each repository independently:

```csharp
await students.AddAsync(student);
await students.CommitAsync();

await courses.AddAsync(course);
await courses.CommitAsync();
```

## Caveats

- Calling `repository.CommitAsync()` while a repository is enlisted throws `InvalidOperationException`. Commit via `uow.CommitAsync()`.
- A repository can be enlisted only once for its current lifetime.
- `RollbackAsync`, `Dispose`, and `DisposeAsync` clear provider tracking for enlisted repositories.

## See also

- [overview.md](overview.md) - repository API and `Once()` / `Suppress*()` reference
- [mutation-pipeline.md](mutation-pipeline.md) - mutation advisors and committed advisors
- [caching.md](caching.md) - cache eviction via committed advisors
- [providers.md](providers.md) - EF Core and LinqToDB UoW implementations
