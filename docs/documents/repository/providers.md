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

Registration is two steps: register the repository for one entity with the closed-generic
`AddRepository`, then configure the underlying data library.

```csharp
services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
        .UseEntityFrameworkCore<AppDbContext>((sp, opts) => opts.UseSqlServer(connectionString));

services.AddRepository<Student, LinqToDbRepository<AppDataConnection, Student>>()
        .UseLinqToDb<AppDataConnection>((sp, opts) => opts.UseSQLite(connectionString));
```

`AddRepository<TEntity, TImplementation>()` is the only registration overload: call it once per
entity type. It registers the repository as a transient `IRepository<TEntity>` and all built-in
advisors with `TryAddEnumerable`.

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

### Count estimates

`EstimateCountAsync` returns `null` until an `IEfCoreCountEstimator<TContext>` is registered. Opt in once
per context using the repository builder; this applies to all repositories using that context type.

The example assumes application-defined `Student` and `AppDbContext` types, an `IServiceCollection`
named `services`, a SQL Server connection string, and the corresponding EF database-provider package.

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Estimation;

services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
        .UseEntityFrameworkCore<AppDbContext>((sp, options) => options.UseSqlServer(connectionString))
        .WithCountEstimates<AppDbContext>(QueryEstimateProvider.SqlServer);
```

The other choices are `QueryEstimateProvider.PostgreSql` and `QueryEstimateProvider.MySql`. The selected
dialect must match the EF provider; SQLite and unmatched providers return `null`. The built-in estimator
accepts mapped entity-root queries with supported filters, ordering, and tracking options. Projections,
paging, grouping, distinct/set operations, explicit includes, raw-SQL roots, `IgnoreQueryFilters`, and
unknown query extensions return `null`. Auto-includes are excluded from the estimate's constant projection.
Translation failures for otherwise supported queries propagate.

The estimator runs after repository build-query advisors and retains EF query filters. It translates
with `CreateDbCommand`, then executes the actual plan SQL through EF's relational command reader
pipeline, preserving typed parameters, the current transaction and timeout. `DbCommandInterceptor`
reader callbacks can inspect, rewrite or reject the command. SQL Server SHOWPLAN ON/OFF uses the
nonquery pipeline; restoration runs independently of request cancellation, and restoration failure
closes the connection and propagates. Interceptors must recognize the actual EXPLAIN/SHOWPLAN SQL,
not assume it is the original SELECT. A connection opened by the estimator is closed on completion.
Use each context serially. The executor is in `src/Schemata.Entity.EntityFrameworkCore/EfCoreEstimateCommand.cs`.

For application-specific visibility or unsupported query shapes, register an implementation of
`Schemata.Entity.EntityFrameworkCore.IEfCoreCountEstimator<TContext>` in DI instead. Its method is
`ValueTask<long?> EstimateAsync<TResult>(TContext context, IQueryable<TResult> query, CancellationToken ct)`.
The context remains repository-owned; custom estimators must preserve query visibility, return `null`
when unsupported, and propagate cancellation and failures. The built-in registration uses `TryAddScoped`,
so an existing custom registration is preserved.

### Mutation methods

`AddAsync` runs the add advisors, then calls `Context.AddAsync(entity)`. `UpdateAsync` runs the
update advisors and — when the entity is concurrency-controlled — records the caller-supplied
`Timestamp` as the original value guarding the write, without minting a new stamp: an already
tracked (non-`Added`) instance keeps its tracker entry with `OriginalValue` set to the incoming
stamp, and a detached instance attaches through `Context.Update`, which captures the incoming
values as the original-value baseline. `RemoveAsync` runs the remove advisors, then calls
`Context.Remove(entity)`. Each write first calls `EnsureWriteUnitOfWork` to open an implicit unit
of work when the repository is not already enlisted; nothing reaches the database until commit
flushes the change tracker.

### Staging and the concurrency baseline

Staging never rotates the stamp. The unit of work mints the next `Timestamp` on every `Modified`
concurrency-controlled entry immediately before `SaveChangesAsync`, so a caller-supplied stale
`Timestamp` aborts the commit with `AbortedException` and repeated updates of one tracked instance
share one rotation at the commit boundary. When the save fails, the unit of work restores each
stamped entry's current value to its original value before running rollback sinks, so a retried
operation starts from the pre-commit state. A successful repository-owned commit permits another
write; that next write enlists a fresh unit-of-work context using the instance's committed token.

### Commit

A standalone `CommitAsync` flushes the implicit unit of work, which calls `SaveChangesAsync` and then
dispatches `IRepositoryCommittedAdvisor<TEntity>`. A `DbUpdateConcurrencyException` from the guarded
update is normalized to `AbortedException`. A unique-constraint violation surfaced by the provider
(SQLite 1555/2067, SQL Server 2601/2627, PostgreSQL `23505`, MySQL 1062 — recognized through
`DatabaseErrorClassifier.IsUniqueConstraintViolation`) is normalized to `AlreadyExistsException`
carrying the entity type and, when the offending entry implements `ICanonicalName`, its canonical
name. When enlisted, the repository commits through the unit of work, which buffers every enlisted
repository's changes and persists them with one `SaveChangesAsync`.

## LinqToDB provider

`LinqToDbRepository<TContext, TEntity>` extends `RepositoryBase<TEntity>` where
`TContext : DataConnection`. A repository creates its connection from a registered `Func<TContext>` and
owns it until enlistment. `UseLinqToDb` composes a private metadata mapping schema per registered
`DataOptions`, preserving application mappings at higher precedence and leaving `MappingSchema.Default` unchanged. The reader
translates `System.ComponentModel.DataAnnotations.Schema` attributes and Schemata's class-level
`[PrimaryKey]` (`Schemata.Abstractions.Entities`) into LinqToDB mapping attributes so a single set of
annotations keys both providers. `[Index]` is parsed but not emitted — LinqToDB mapping has no index
concept, so create indexes through the application's schema-management path. Property discovery
mirrors EF Core model defaults: a property maps when it has a public instance getter, a setter
(private is sufficient), and no index parameters. Explicit interface implementations, private getters,
and read-only properties are excluded unless `[Column]`, class-level `[PrimaryKey]` membership, or
`[DatabaseGenerated(Identity)]` marks the member as an intended column.

### Table name resolution

Resolved in the constructor: the `[Table]` attribute name when present, otherwise the entity type name
pluralized through Humanizer.

### Mutation methods

LinqToDB executes mutations immediately inside the open transaction. `AddAsync` runs the add advisors,
calls `EnsureWriteUnitOfWork`, then `InsertAsync`. `AddRangeAsync` runs the add advisors per entity and
persists the survivors with one bulk-copy round trip. `UpdateAsync` runs the update advisors, then calls
`UpdateOptimisticWithRefreshAsync` for concurrency-controlled entities and `UpdateAsync` otherwise.
The guarded update regenerates the token inside the UPDATE statement and writes the regenerated value
back onto the entity, so the instance's token equals the value this update wrote; a zero-row result
raises `AbortedException`. `RemoveAsync` runs the remove advisors, then `DeleteOptimisticAsync` for
concurrency-controlled entities — the delete predicates on the primary key and the instance's token,
and a zero-row result raises `AbortedException` — or `DeleteAsync` otherwise.

Because writes execute immediately, a query later in the same transaction observes the repository's own
uncommitted writes — read-your-own-writes. The transaction opens lazily on the first access of the unit
of work's `Context`.

### Commit

`CommitAsync` commits the transaction and dispatches committed advisors. A unique-constraint violation
recognized by `DatabaseErrorClassifier.IsUniqueConstraintViolation` is normalized to a bare
`AlreadyExistsException` (no canonical name is attached). On a commit failure it runs the
rollback sinks and disposes the transaction. Rollback during commit-failure or disposal cleanup is
swallowed so it does not mask the original error; when an `ILogger` is registered, the swallowed failure
is logged at warning level.

### EstimateCountAsync

`LinqToDbRepository` estimates the query after build-query advisors using native parameter binding.
Supported plan backends are:

| Backend | Plan and result cardinality |
| --- | --- |
| PostgreSQL | `EXPLAIN (FORMAT JSON)`, root `Plan Rows`. |
| MySQL | `EXPLAIN FORMAT=JSON`, `rows_produced_per_join` for a table or final nested-loop table, including ordering wrappers. |
| SQL Server | `SET SHOWPLAN_XML ON`, root result `RelOp.EstimateRows`, then `SET SHOWPLAN_XML OFF`. |

These commands request optimizer estimates; they use neither `ANALYZE` nor an exact count. MySQL shapes
with grouping, distinct, windowing, set operations, semi/anti joins, or LINQ pagination return `null`.
SQL Server requires a connection that stays open across SHOWPLAN commands; `CloseAfterUse` contexts
return `null`. Restoration runs even after cancellation or query failure. A restoration failure closes
the connection and propagates the error. Concurrent use of that connection is unsupported.

SQLite reads the first cardinality integer in `sqlite_stat1` only for a full-table identity query.
Filters, ordering, paging, projections, query-filter mappings, inheritance mappings, calculated members,
and non-default schema/database mappings are unsupported. Missing statistics return `null`.
MariaDB and unrecognized providers also return `null`.

Both repository providers share plan parsing in
`src/Schemata.Entity.Repository/Estimation/QueryPlanEstimate.cs`. Well-formed unsupported plans return
`null`; malformed plan data and invalid cardinalities fail explicitly. Database errors and cancellation
propagate, and neither provider falls back to `CountAsync` or `LongCountAsync`.

## Query cache keys

Both providers implement `IQueryCacheKeyProvider`. `GetQueryCacheKey` derives the key from the command
the provider would execute — `CreateDbCommand` for EF Core, `ToSqlQuery(InlineParameters: false)` for
LinqToDB — together with the data source identity and the ordered typed parameters, composed through
`QueryCacheKey.Create`, which hashes internally so connection identity, command text, and parameter
values never survive into the key. EF Core hashes the full connection string next to the connection
type, `DataSource`, and `Database`; LinqToDB hashes the full connection string. SQLite in-memory
lifetimes (`Mode=Memory`, `Data Source=:memory:`, empty data source) and EF Core non-relational
providers return `null`, which disables caching for the query. Translated query filters and
parameters participate in the identity. `DbCommandInterceptor` callbacks do not run while an EF
key is built: applications must suppress caching when session state or interceptors change query
meaning outside that identity. The query cache adds projection structure and terminal operation.

## Provider comparison

| Aspect                             | EF Core                                             | LinqToDB                                              |
| ---------------------------------- | --------------------------------------------------- | ----------------------------------------------------- |
| Context type                       | `DbContext`                                         | `DataConnection`                                      |
| Change tracking                    | Full EF Core tracker                                | None                                                  |
| Write execution                    | Buffered in the tracker, flushed at commit          | Immediate, inside the open transaction                |
| Read-your-own-writes before commit | No                                                  | Yes                                                   |
| `UpdateAsync`                      | Caller stamp becomes the original value; the unit of work rotates at commit | `UpdateOptimisticWithRefreshAsync` or `UpdateAsync` |
| Concurrency on update              | `DbUpdateConcurrencyException` → `AbortedException` | zero-row guarded update → `AbortedException`          |
| Concurrency on delete              | guarded delete at commit → `AbortedException`       | zero-row `DeleteOptimisticAsync` → `AbortedException` |
| Unique-constraint violation        | `AlreadyExistsException` with type + canonical name | bare `AlreadyExistsException`                         |
| `EstimateCountAsync`               | opt-in plan estimate or custom estimator; otherwise `null` | per-backend plan/statistics estimate or `null` |

## Extension points

To add a provider, inherit from `RepositoryBase<TEntity>` and implement its abstract members
(`AsQueryable`, `AddAsync`, `UpdateAsync`, `RemoveAsync`, `CreateUnitOfWork`, `AttachContext`,
`DisposeContext`, `BuildQueryAsync`, and the query executors). Satisfying `IRepository<TEntity>` also
satisfies the non-generic `IRepository` surface that infrastructure code depends on.

## Caveats

- **Restaged instances retain the original concurrency baseline** — the first staging determines
  the expected version for the pending update. Subsequent staging of the same tracked instance
  preserves that baseline; the unit of work mints the next token once, at commit.
- **Rollback does not restore instance state** — after a rollback the entity's properties keep the
  values the caller set (EF Core additionally restores the concurrency stamp to its original value
  when the save itself fails). Reload the instance from a fresh repository scope before reusing it;
  a repository whose unit of work has completed must itself be resolved from a fresh scope.
- **Uncommitted-read visibility differs** — EF Core buffers writes until commit; LinqToDB executes them
  immediately. Provider-agnostic code must not depend on reading its own uncommitted writes through the
  EF Core provider.
- **LinqToDB mapping is local to registered options.** Repeated registration retains the existing
  options singleton; independent registrations compose their own Schemata metadata reader.
- **Translated uniqueness errors retain their provider exception internally.** `InnerException`
  is available for diagnostics; public error envelopes contain only the classified resource details.

## See also

- [overview.md](overview.md) — `IRepository<TEntity>` API and suppression scopes
- [unit-of-work.md](unit-of-work.md) — enlistment and committed advisors
- [entity/traits.md](../entity/traits.md) — `IConcurrency` and provider-level enforcement
