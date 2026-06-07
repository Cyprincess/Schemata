# Repository Providers

A repository provider is the concrete `RepositoryBase<TEntity>` implementation that translates the repository abstraction into actual database operations. Schemata ships two providers: one backed by Entity Framework Core and one backed by LINQ to DB. Both implement the same `IRepository<TEntity>` interface and participate in the same advisor pipelines, so application code is provider-agnostic.

## Where the code lives

| Item | Path |
|---|---|
| `EntityFrameworkCoreRepository<TContext,TEntity>` | `src/Schemata.Entity.EntityFrameworkCore/EntityFrameworkCoreRepository.cs` |
| `LinQ2DbRepository<TContext,TEntity>` | `src/Schemata.Entity.LinqToDB/LinQ2DbRepository.cs` |
| `AddRepository` extension | `src/Schemata.Entity.Repository/Extensions/ServiceCollectionExtensions.cs` |

## Registration

Provider registration is a two-step process: register the repository implementation type with `AddRepository`, then configure the underlying data access library.

```csharp
// Entity Framework Core
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>((sp, opts) => opts.UseSqlServer(connectionString));

// LINQ to DB
services.AddRepository(typeof(LinQ2DbRepository<,>))
        .UseLinqToDb<AppDataConnection>((sp, opts) => opts.UseSQLite(connectionString));
```

`AddRepository` validates that the implementation type implements both `IRepository` and `IRepository<>`, registers it as open-generic scoped, and registers all built-in advisors via `TryAddEnumerable`.

## Entity Framework Core provider

**Package:** `Schemata.Entity.EntityFrameworkCore`

`EntityFrameworkCoreRepository<TContext, TEntity>` extends `RepositoryBase<TEntity>` where `TContext : DbContext`.

### Query methods

`ListAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, and `LongCountAsync` all call `BuildQueryAsync` internally, which:

1. Creates a `QueryContainer` from `AsQueryable()`.
2. Runs `IRepositoryBuildQueryAdvisor<TEntity>` pipeline on the container.
3. Applies the caller's predicate via `BuildQuery(container.Query, predicate)`.
4. Returns the composed `IQueryable<TResult>`.

Scalar methods then run `IRepositoryQueryAdvisor` (cache hit check), execute the query, and run `IRepositoryResultAdvisor` (cache store).

### Mutation methods

`AddAsync` runs `IRepositoryAddAdvisor<TEntity>`, then calls `Context.AddAsync(entity)`.

`UpdateAsync` runs `IRepositoryUpdateAdvisor<TEntity>`, then calls `Detach(entity)` followed by `Context.Update(entity)`.

`RemoveAsync` runs `IRepositoryRemoveAdvisor<TEntity>`, then calls `Context.Remove(entity)`.

### Detach before Update

`UpdateAsync` clears the change tracker entry for `entity` before calling `Context.Update`. EF Core enforces a single tracked instance per key in the `DbContext`; whenever any other code in the same scope has already materialised the same row, `Context.Update(entity)` would throw "The instance of entity type 'X' cannot be tracked because another instance with the same key value is already being tracked."

Several routine paths produce a tracked instance:

- A repository-layer advisor that calls back into `IRepository<TEntity>.GetAsync` or `SingleOrDefaultAsync` to read the stored row (for example a concurrency stamp check, an ownership check, or a custom audit).
- A mapper that constructs an entity by loading and copying from a tracked source.
- An earlier handler stage that loaded the same row (the resource update pipeline first loads, then runs advisors, then writes).
- Application code that issued a side query inside the same scope.

Detaching is the smallest correct response to any of these. The call is not specific to one advisor; it keeps `UpdateAsync` safe under every combination of advisors and call sites that may have populated the tracker.

### CommitAsync

Calls `Context.SaveChangesAsync(ct)`, then `DrainAfterCommitAsync(ct)`. Throws `InvalidOperationException` if called while a unit of work is active.

### SearchAsync

Not implemented; throws `NotImplementedException`. Use `ListAsync` with a filter expression instead.

## LINQ to DB provider

**Package:** `Schemata.Entity.LinqToDB`

`LinQ2DbRepository<TContext, TEntity>` extends `RepositoryBase<TEntity>` where `TContext : DataConnection`.

### Table name resolution

Determined in the constructor: checks for `[Table]` attribute on the entity type, falls back to the pluralized entity type name via Humanizer.

### Mutation methods

When a unit of work is active, mutations execute SQL immediately (`Context.InsertAsync`, `Context.UpdateAsync`, `Context.DeleteAsync`). Without a unit of work, mutations are queued as `PendingOperation` structs and executed in `CommitAsync` wrapped in a local transaction to keep the commit atomic.

`Detach` is a no-op because LINQ to DB does not track entity state.

### CommitAsync

When no unit of work is active and there are pending operations, wraps them in a local transaction: begins, executes each pending operation, commits. On failure, rolls back and rethrows. After commit, drains the after-commit queue.

### SearchAsync

Not implemented; throws `NotImplementedException`.

## Provider comparison

| Aspect | EF Core | LINQ to DB |
|---|---|---|
| Context type | `DbContext` | `DataConnection` |
| Change tracking | Full EF Core tracker | None; `Detach` is a no-op |
| Mutation style | Staged in tracker, flushed by `SaveChangesAsync` | Immediate SQL or queued pending ops |
| `UpdateAsync` | `Detach(entity)` then `Context.Update(entity)` | `Context.UpdateAsync(entity)` directly |
| `CommitAsync` | `SaveChangesAsync` + drain | Local transaction over pending ops + drain |
| `SearchAsync` | `NotImplementedException` | `NotImplementedException` |

## Extension points

To implement a custom provider, inherit from `RepositoryBase<TEntity>` and implement the abstract members. The non-generic `IRepository` surface comes for free via the base class delegation.

## Caveats

- **EF Core `UpdateAsync` detach**: required whenever the change tracker has already seen the same row in the current scope. See [Detach before Update](#detach-before-update).
- **`SearchAsync`**: both providers throw `NotImplementedException`. Use `ListAsync` with a filter expression.

## See also

- [overview.md](overview.md) — `IRepository<TEntity>` API and `Once()` / `Suppress*()` reference
- [mutation-pipeline.md](mutation-pipeline.md) — advisor chains for add, update, remove
- [unit-of-work.md](unit-of-work.md) — `BeginWork`, `CommitAsync`, and the after-commit queue
- [entity/traits.md](../entity/traits.md) — `IConcurrency` and the concurrency advisor
