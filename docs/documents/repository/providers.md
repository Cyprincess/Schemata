# Repository Providers

A repository provider is the concrete `RepositoryBase<TEntity>` implementation that translates the repository abstraction into actual database operations. Schemata ships two providers: one backed by Entity Framework Core and one backed by LINQ to DB. Both implement the same `IRepository<TEntity>` interface and participate in the same advisor pipelines, so application code is provider-agnostic.

## Where the code lives

| Item | Path |
|---|---|
| `EntityFrameworkCoreRepository<TContext,TEntity>` | `src/Schemata.Entity.EntityFrameworkCore/EntityFrameworkCoreRepository.cs` |
| `LinqToDbRepository<TContext,TEntity>` | `src/Schemata.Entity.LinqToDB/LinqToDbRepository.cs` |
| `AddRepository` extension | `src/Schemata.Entity.Repository/Extensions/ServiceCollectionExtensions.cs` |

## Registration

Provider registration is a two-step process: register the repository implementation type with `AddRepository`, then configure the underlying data access library.

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>((sp, opts) => opts.UseSqlServer(connectionString));

services.AddRepository(typeof(LinqToDbRepository<,>))
        .UseLinqToDb<AppDataConnection>((sp, opts) => opts.UseSQLite(connectionString));
```

`AddRepository` validates that the implementation type implements both `IRepository` and `IRepository<>`, registers it as open-generic transient, and registers all built-in advisors via `TryAddEnumerable`.

## Entity Framework Core provider

**Package:** `Schemata.Entity.EntityFrameworkCore`

`EntityFrameworkCoreRepository<TContext, TEntity>` extends `RepositoryBase<TEntity>` where `TContext : DbContext`. Each repository owns a context from `IDbContextFactory<TContext>` until it is enlisted into a unit of work.

### Query methods

`ListAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, and `LongCountAsync` all call `BuildQueryAsync` internally, which:

1. Creates a `QueryContainer` from `AsQueryable()`.
2. Runs `IRepositoryBuildQueryAdvisor<TEntity>` pipeline on the container.
3. Applies the caller's predicate via `BuildQuery(container.Query, predicate)`.
4. Returns the composed `IQueryable<TResult>`.

Scalar methods then run `IRepositoryQueryAdvisor` (cache hit check), execute the query, and run `IRepositoryResultAdvisor` (cache store).

### Mutation methods

`AddAsync` runs `IRepositoryAddAdvisor<TEntity>`, tracks the entity in the commit snapshot, then calls `Context.AddAsync(entity)`.

`UpdateAsync` runs `IRepositoryUpdateAdvisor<TEntity>`, tracks the entity in the commit snapshot, then calls `Detach(entity)` followed by `Context.Update(entity)`.

`RemoveAsync` runs `IRepositoryRemoveAdvisor<TEntity>`, tracks the entity in the commit snapshot, then calls `Context.Remove(entity)`.

### Detach before Update

`UpdateAsync` clears the change tracker entry for `entity` before calling `Context.Update`. EF Core enforces a single tracked instance per key in the `DbContext`; whenever any other code in the same context has already materialised the same row, `Context.Update(entity)` would throw "The instance of entity type 'X' cannot be tracked because another instance with the same key value is already being tracked."

### CommitAsync

Standalone `CommitAsync` snapshots tracked changes, calls `Context.SaveChangesAsync(ct)`, then dispatches `IRepositoryCommittedAdvisor<TEntity>` with the snapshot. It throws `InvalidOperationException` if the repository is enlisted in a unit of work.

When enlisted, the repository adopts the unit of work's context and registers committed/rollback sinks. The unit of work calls the committed sink after the transaction commits and the rollback sink when the transaction rolls back or is disposed.

### SearchAsync

Not implemented; throws `NotImplementedException`. Use `ListAsync` with a filter expression instead.

## LINQ to DB provider

**Package:** `Schemata.Entity.LinqToDB`

`LinqToDbRepository<TContext, TEntity>` extends `RepositoryBase<TEntity>` where `TContext : DataConnection`. Each repository owns a `DataConnection` from a registered `Func<TContext>` until it is enlisted into a unit of work.

### Table name resolution

Determined in the constructor: checks for `[Table]` attribute on the entity type, falls back to the pluralized entity type name via Humanizer.

### Mutation methods

When enlisted in a unit of work, mutations add pending operations to the unit of work. Without a unit of work, mutations are queued as `PendingOperation` values on the repository and executed in `CommitAsync` wrapped in a local transaction.

`Detach` is a no-op because LINQ to DB does not track entity state.

### CommitAsync

Standalone `CommitAsync` snapshots tracked changes, executes pending operations in a local transaction, commits, then dispatches `IRepositoryCommittedAdvisor<TEntity>`. On failure, it rolls back and rethrows. It throws `InvalidOperationException` if the repository is enlisted in a unit of work.

### SearchAsync

Not implemented; throws `NotImplementedException`.

## Provider comparison

| Aspect | EF Core | LINQ to DB |
|---|---|---|
| Context type | `DbContext` | `DataConnection` |
| Context ownership | Factory-created per repository; UoW-owned after enlistment | Factory-created per repository; UoW-owned after enlistment |
| Change tracking | Full EF Core tracker | None; `Detach` is a no-op |
| Mutation style | Staged in tracker, flushed by `SaveChangesAsync` | Queued pending ops, executed by commit boundary |
| `UpdateAsync` | `Detach(entity)` then `Context.Update(entity)` | Queues `Context.UpdateAsync(entity)` |
| `CommitAsync` | `SaveChangesAsync` + committed advisor dispatch | Local transaction over pending ops + committed advisor dispatch |
| `SearchAsync` | `NotImplementedException` | `NotImplementedException` |

## Extension points

To implement a custom provider, inherit from `RepositoryBase<TEntity>` and implement the abstract members. Because `IRepository<TEntity>` inherits from the non-generic `IRepository`, satisfying the generic interface also satisfies the entity-agnostic surface that framework infrastructure code depends on.

## Caveats

- **EF Core `UpdateAsync` detach**: required whenever the change tracker has already seen the same row in the current context. See [Detach before Update](#detach-before-update).
- **`SearchAsync`**: both providers throw `NotImplementedException`. Use `ListAsync` with a filter expression.
- **Uncommitted-read visibility differs (weak consistency)**: EF Core buffers writes in the change tracker and flushes them at `CommitAsync`, so a query before commit does not observe pending inserts or updates. LINQ to DB executes mutations immediately inside the open transaction, so a later query in the same unit of work does observe them. Provider-agnostic code must not depend on reading its own uncommitted writes; the mainstream write-then-commit path is unaffected.
- **Reusable after a failed commit**: when `CommitAsync` throws, both providers reset to a reusable state. EF Core clears the change tracker and the staged work; LINQ to DB rolls back and disposes the transaction so the next mutation opens a fresh one. The caller may stage and commit new work on the same repository.
- **Optional cleanup diagnostics**: rollback and dispose run inside cleanup `catch` blocks that intentionally do not rethrow (the transaction may already be complete). When an `ILogger` is registered, these failures are logged at warning level; otherwise they are silent.

## See also

- [overview.md](overview.md) - `IRepository<TEntity>` API and `Once()` / `Suppress*()` reference
- [mutation-pipeline.md](mutation-pipeline.md) - advisor chains for add, update, remove, and commit
- [unit-of-work.md](unit-of-work.md) - explicit enlistment and committed advisors
- [entity/traits.md](../entity/traits.md) - `IConcurrency` and the concurrency advisor
