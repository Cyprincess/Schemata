# Repository Providers

A provider is the concrete `RepositoryBase<TEntity>` implementation that translates the repository
abstraction into database operations. Schemata ships two: one over Entity Framework Core and one over
LinqToDB. Both implement `IRepository<TEntity>` and run the same advisor pipelines, so application code
stays provider-agnostic.

## Where the code lives

| Item                                   | Path                                                                       |
| -------------------------------------- | -------------------------------------------------------------------------- |
| `EfCoreRepository<TContext,TEntity>`   | `src/Schemata.Entity.EntityFrameworkCore/EfCoreRepository.cs`              |
| `EfCoreUnitOfWork<TContext>`           | `src/Schemata.Entity.EntityFrameworkCore/EfCoreUnitOfWork.cs`              |
| `LinqToDbRepository<TContext,TEntity>` | `src/Schemata.Entity.LinqToDB/LinqToDbRepository.cs`                       |
| `LinqToDbUnitOfWork<TContext>`         | `src/Schemata.Entity.LinqToDB/LinqToDbUnitOfWork.cs`                       |
| `AddRepository` extension              | `src/Schemata.Entity.Repository/Extensions/ServiceCollectionExtensions.cs` |

## Registration

Registration is two steps: register the repository type with `AddRepository`, then configure the
underlying data library.

```csharp
services.AddRepository(typeof(EfCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>((sp, opts) => opts.UseSqlServer(connectionString));

services.AddRepository(typeof(LinqToDbRepository<,>))
        .UseLinqToDb<AppDataConnection>((sp, opts) => opts.UseSQLite(connectionString));
```

`AddRepository(Type)` validates that the type implements `IRepository<>`, registers it as an
open-generic transient, and registers all built-in advisors with `TryAddEnumerable`. The closed-generic
overload `AddRepository<TEntity, EfCoreRepository<TContext, TEntity>>()` registers one entity's
repository.

## Entity Framework Core provider

`EfCoreRepository<TContext, TEntity>` extends `RepositoryBase<TEntity>` where `TContext : DbContext`. A
repository creates its context from `IDbContextFactory<TContext>` and owns it until it enlists in a unit
of work. `UseEntityFrameworkCore<TContext>(configure)` registers that factory via
`AddDbContextFactory<TContext>`; a two-type overload registers a factory for an implementation context
constrained to a shared abstraction.

### Query methods

`ListAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, and
`LongCountAsync` call `BuildQueryAsync`, which creates a `QueryContainer` from `AsQueryable()`, runs the
build-query advisors, and applies the caller's predicate. When a build-query advisor returns `Block`,
the query is replaced with `q.Where(_ => false)` so it returns no rows. Scalar methods then run the
query advisor (cache-hit short-circuit), execute, and run the result advisor (cache store).

### Mutation methods

`AddAsync` runs the add advisors, then calls `Context.AddAsync(entity)`. `UpdateAsync` runs the update
advisors, detaches any existing tracker entry for the entity, calls `Context.Update(entity)`, and — when
the entity is concurrency-controlled — sets the current `Timestamp` to a fresh GUID. `RemoveAsync` runs
the remove advisors, then calls `Context.Remove(entity)`. Each write first calls `EnsureWriteUnitOfWork`
to open an implicit unit of work when the repository is not already enlisted; nothing reaches the
database until commit flushes the change tracker.

### Detach before update

`UpdateAsync` sets the incoming entity's tracker state to `Detached` before `Context.Update`. EF Core
allows one tracked instance per key in a `DbContext`; when other code in the same context has already
materialized that row, `Context.Update(entity)` would otherwise throw "another instance with the same
key value is already being tracked."

### Commit

A standalone `CommitAsync` flushes the implicit unit of work, which calls `SaveChangesAsync` and then
dispatches `IRepositoryCommittedAdvisor<TEntity>`. A `DbUpdateConcurrencyException` from the guarded
update is normalized to `AbortedException`. When enlisted, the repository commits through the unit
of work, which buffers every enlisted repository's changes and persists them with one
`SaveChangesAsync`.

## LinqToDB provider

`LinqToDbRepository<TContext, TEntity>` extends `RepositoryBase<TEntity>` where
`TContext : DataConnection`. A repository creates its connection from a registered `Func<TContext>` and
owns it until enlistment. `UseLinqToDb` also registers a metadata reader
(`SystemComponentModelDataAnnotationsSchemaAttributeReader`) on the process-wide `MappingSchema.Default`,
translating `System.ComponentModel.DataAnnotations.Schema` attributes and the EF Core class-level
`[PrimaryKey]` into LinqToDB mapping attributes so a single set of annotations keys both providers.

### Table name resolution

Resolved in the constructor: the `[Table]` attribute name when present, otherwise the entity type name
pluralized through Humanizer.

### Mutation methods

LinqToDB executes mutations immediately inside the open transaction. `AddAsync` runs the add advisors,
calls `EnsureWriteUnitOfWork`, then `InsertAsync`. `AddRangeAsync` runs the add advisors per entity and
persists the survivors with one bulk-copy round trip. `UpdateAsync` runs the update advisors, then calls
`UpdateOptimisticAsync` for concurrency-controlled entities (raising `AbortedException` on a
zero-row result) or `UpdateAsync` otherwise. `RemoveAsync` runs the remove advisors, then `DeleteAsync`.

Because writes execute immediately, a query later in the same transaction observes the repository's own
uncommitted writes — read-your-own-writes. The transaction opens lazily on the first access of the unit
of work's `Context`.

### Commit

`CommitAsync` commits the transaction and dispatches committed advisors. On a commit failure it runs the
rollback sinks and disposes the transaction. Rollback during commit-failure or disposal cleanup is
swallowed so it does not mask the original error; when an `ILogger` is registered, the swallowed failure
is logged at warning level.

## Provider comparison

| Aspect                             | EF Core                                             | LinqToDB                                              |
| ---------------------------------- | --------------------------------------------------- | ----------------------------------------------------- |
| Context type                       | `DbContext`                                         | `DataConnection`                                      |
| Change tracking                    | Full EF Core tracker                                | None                                                  |
| Write execution                    | Buffered in the tracker, flushed at commit          | Immediate, inside the open transaction                |
| Read-your-own-writes before commit | No                                                  | Yes                                                   |
| `UpdateAsync`                      | Detach, `Context.Update`, bump token                | `UpdateOptimisticAsync` or `UpdateAsync`              |
| Concurrency on update              | `DbUpdateConcurrencyException` → `AbortedException` | zero-row `UpdateOptimisticAsync` → `AbortedException` |

## Extension points

To add a provider, inherit from `RepositoryBase<TEntity>` and implement its abstract members
(`AsQueryable`, `AddAsync`, `UpdateAsync`, `RemoveAsync`, `CreateUnitOfWork`, `AttachContext`,
`DisposeContext`, `BuildQueryAsync`, and the query executors). Satisfying `IRepository<TEntity>` also
satisfies the non-generic `IRepository` surface that infrastructure code depends on.

## Caveats

- **EF Core update detach** — required whenever the change tracker has already seen the same row in the
  current context.
- **Uncommitted-read visibility differs** — EF Core buffers writes until commit; LinqToDB executes them
  immediately. Provider-agnostic code must not depend on reading its own uncommitted writes through the
  EF Core provider.
- **LinqToDB metadata reader is process-wide** — `UseLinqToDb` mutates `MappingSchema.Default`, so the
  attribute translation applies to every `DataConnection` in the process. Repeated calls append further
  reader instances, which is harmless because LinqToDB resolves attributes through any registered reader.

## See also

- [overview.md](overview.md) — `IRepository<TEntity>` API and suppression scopes
- [unit-of-work.md](unit-of-work.md) — enlistment and committed advisors
- [entity/traits.md](../entity/traits.md) — `IConcurrency` and provider-level enforcement
