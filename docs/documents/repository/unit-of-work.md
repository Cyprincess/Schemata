# Unit of Work

The unit-of-work pattern coordinates multiple repository mutations within a single database transaction. `IUnitOfWork` provides explicit begin/commit/rollback control, while the repository layer automatically detects an active unit of work and delegates commit responsibility.

## Interface

```csharp
public interface IUnitOfWork : IDisposable
{
    bool IsActive { get; }
    void Begin();
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

public interface IUnitOfWork<TContext> : IUnitOfWork
{
}
```

| Member         | Purpose                                                     |
| -------------- | ----------------------------------------------------------- |
| `IsActive`     | `true` when a transaction has been started and not yet committed or rolled back |
| `Begin()`      | Starts a new database transaction. Throws if already active |
| `CommitAsync`  | Persists changes and commits the transaction                |
| `RollbackAsync`| Rolls back without persisting changes                       |
| `Dispose()`    | Rolls back if still active (safe disposal)                  |

`IUnitOfWork<TContext>` carries a type parameter bound to the concrete data context (`DbContext` or `DataConnection`), enabling multiple provider types to coexist in the same DI container.

## How repositories interact with Unit of Work

Every `RepositoryBase<TEntity>` constructor accepts an optional `IUnitOfWork?` parameter. When a unit of work is provided:

1. The repository stores it as `protected virtual IUnitOfWork? UnitOfWork`.
2. During `CommitAsync`, the repository checks `UnitOfWork?.IsActive`:
   - **If active** — the repository **throws** `InvalidOperationException`, preventing accidental commits inside a unit of work. Commit must happen via `uow.CommitAsync()`.
   - **If inactive or null** — the repository commits directly.
3. `BeginWork()` is available on `IRepository` to explicitly create and return the unit of work via `UnitOfWork.Begin()`.

This design means you can either commit individually or batch operations under a single transaction:

```csharp
// Individual commits (each is its own transaction)
await orders.AddAsync(order);
await orders.CommitAsync();

// Explicit unit of work (single transaction)
using var uow = orders.BeginWork();
await orders.AddAsync(order1);
await items.AddAsync(lineItem1);
await items.AddAsync(lineItem2);
await uow.CommitAsync();  // one transaction, one round-trip
```

When repositories share the same scoped `DbContext` (or `DataConnection`), a call to `BeginWork()` on any of them creates a transaction that covers all tracked changes — even those made by sibling repositories.

```csharp
public class OrderService(
    IRepository<Order> orders,
    IRepository<OrderItem> items)
{
    public async Task PlaceAsync(Order order, List<OrderItem> lines)
    {
        using var uow = orders.BeginWork();
        await orders.AddAsync(order);
        foreach (var line in lines) {
            await items.AddAsync(line);
        }
        await uow.CommitAsync();
    }
}
```

Because `IUnitOfWork<TContext>` is registered as **scoped**, all injections within the same HTTP request resolve the same instance. The `IsActive` flag prevents nested transactions.

## EF Core implementation

`EfCoreUnitOfWork<TContext> : IUnitOfWork<TContext>` where `TContext : DbContext`.

| Method         | Behavior                                                           |
| -------------- | ------------------------------------------------------------------ |
| `Begin()`      | Calls `_context.Database.BeginTransaction()`                       |
| `CommitAsync`  | Calls `SaveChangesAsync` then `_transaction.CommitAsync`           |
| `RollbackAsync`| Calls `_transaction.RollbackAsync`                                 |
| `Dispose()`    | Rolls back if still active                                         |

## Registration

Chain `.WithUnitOfWork<TContext>()` on the repository builder to enable `IUnitOfWork<TContext>`:

```csharp
// EF Core
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
    .UseEntityFrameworkCore<AppDbContext>(configure)
    .WithUnitOfWork<AppDbContext>();

// LINQ to DB
services.AddRepository(typeof(LinQ2DbRepository<,>))
    .UseLinqToDb<AppDataConnection>(configure)
    .WithUnitOfWork<AppDataConnection>();
```

`.WithUnitOfWork<TContext>()` registers `IUnitOfWork<TContext>` as `Scoped` using `TryAdd`, so a prior registration takes precedence. `BeginWork()` requires the registration to be present — calling it on an unconfigured repository throws `InvalidOperationException` to avoid silent no-op transactions.

## LINQ to DB implementation

`LinqToDbUnitOfWork<TContext> : IUnitOfWork<TContext>` where `TContext : DataConnection`.

| Method         | Behavior                                                      |
| -------------- | ------------------------------------------------------------- |
| `Begin()`      | Calls `_context.BeginTransaction()`                           |
| `CommitAsync`  | Calls `_transaction.CommitAsync` (LINQ to DB auto-flushes)    |
| `RollbackAsync`| Calls `_transaction.RollbackAsync`                            |
| `Dispose()`    | Rolls back if still active                                    |

Like the EF Core implementation, `.WithUnitOfWork<TDataConnection>()` must be called explicitly to register `IUnitOfWork<TDataConnection>`.

## Key differences between EF Core and LINQ to DB UoW

| Aspect                 | EF Core                                  | LINQ to DB                      |
| ---------------------- | ---------------------------------------- | ------------------------------- |
| `CommitAsync`          | Calls `SaveChangesAsync` + `CommitAsync` | Calls `CommitAsync` only (auto-flush) |
| Transaction type       | `IDbContextTransaction`                  | `DataConnectionTransaction`     |
| Change detection       | Tracker-based; `SaveChangesAsync` computes diffs | Mutation methods execute SQL immediately |
| Registration           | `.WithUnitOfWork<AppDbContext>()`        | `.WithUnitOfWork<AppDataConnection>()` |

## Scoped repository sharing

Even without an explicit `IUnitOfWork`, repositories sharing the same scoped `DbContext` naturally batch operations. A single `CommitAsync` on any repository persists all pending changes to that context:

```csharp
// Both repos share the same AppDbContext (scoped)
await students.AddAsync(student);
await courses.AddAsync(course);
await students.CommitAsync();  // flushes BOTH student and course
```
