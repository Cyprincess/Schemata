# Repository Overview

`IRepository<TEntity>` is the data-access abstraction in Schemata. It wraps a backing store (Entity
Framework Core, LinqToDB, or a custom provider) behind a uniform API and routes every read and write
through an advisor pipeline that handles timestamps, concurrency stamps, soft-delete, validation,
uniqueness, and canonical-name generation. A non-generic `IRepository` carries the entity-agnostic
surface — `AdviceContext`, `Begin`/`Join`/`CommitAsync`, and the `Suppress*` scopes — so coordination
code that does not know `TEntity` depends on the non-generic interface; `IRepository<TEntity>`
extends it with typed CRUD members.

## Where the code lives

| Item                                                         | Path                                                                       |
| ------------------------------------------------------------ | -------------------------------------------------------------------------- |
| `IRepository`, `IRepository<TEntity>`                        | `src/Schemata.Entity.Repository/IRepository.cs`                            |
| `RepositoryBase<TEntity>`                                    | `src/Schemata.Entity.Repository/RepositoryBase.cs`                         |
| `IUnitOfWork`, `IUnitOfWork<TContext>`                       | `src/Schemata.Entity.Repository/IUnitOfWork.cs`                            |
| `QueryContainer<TEntity>`, `QueryContext<TEntity,TResult,T>` | `src/Schemata.Entity.Repository/`                                          |
| Built-in advisors                                            | `src/Schemata.Entity.Repository/Advisors/`                                 |
| DI registration                                              | `src/Schemata.Entity.Repository/Extensions/ServiceCollectionExtensions.cs` |

## Query API

Every query method takes an optional `Func<IQueryable<TEntity>, IQueryable<TResult>>` transform and
runs the build-query advisors before executing.

```csharp
IAsyncEnumerable<TResult> ListAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);

ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);
ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);
ValueTask<bool>     AnyAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);
ValueTask<int>      CountAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);
ValueTask<long>     LongCountAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);
ValueTask<long?>    EstimateCountAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);

// Key-based lookup
ValueTask<TEntity?> GetAsync(TEntity? entity, CancellationToken ct = default);
ValueTask<TResult?> GetAsync<TResult>(TEntity? entity, CancellationToken ct = default);
ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default);
ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default);
```

The `predicate` is a query transformation, not a boolean expression, so a single lambda can chain
`Where`, `Select`, `OrderBy`, `Skip`, and `Take`:

```csharp
var page = repository.ListAsync<BookDto>(q =>
    q.Where(b => b.Price > 10)
     .OrderBy(b => b.Name)
     .Select(b => new BookDto(b.Uid, b.Name))
     .Skip(20)
     .Take(10));
```

When `predicate` is `null`, the query falls through to `OfType<TResult>()` on the advisor-processed
queryable. `GetAsync` reads the key properties off the supplied entity and delegates to `FindAsync`;
`FindAsync` builds a key equality predicate and routes through `SingleOrDefaultAsync`.

### EstimateCountAsync

`EstimateCountAsync` returns a nullable estimate of the scoped query's result count. The default
interface and base implementations return `null`. Unsupported providers or query shapes also return
`null`; callers that require exact totals must explicitly choose `CountAsync` or `LongCountAsync`.
Estimation never falls back to either exact-count method. Cancellation, database failures, and invalid
plan data propagate to the caller.

EF Core provides per-context opt-in through `WithCountEstimates<TContext>(QueryEstimateProvider)` or
a custom `IEfCoreCountEstimator<TContext>`. The built-in estimator uses public EF APIs to create a
parameterized command; command execution bypasses EF command interceptors, so opt-in requires a review
of security-sensitive command rewrites. LinqToDB selects estimation by its registered database provider.

The plan backends are PostgreSQL `EXPLAIN (FORMAT JSON)`, MySQL `EXPLAIN FORMAT=JSON`, and SQL Server
`SHOWPLAN_XML`. These request optimizer plans without executing the SELECT for a count. LinqToDB also
supports SQLite statistics for a restricted unfiltered table query and returns `null` for MariaDB or
unrecognized provider names. See [Repository Providers](providers.md) for activation, query-shape limits,
and connection requirements.

## Mutation API

```csharp
Task AddAsync(TEntity entity, CancellationToken ct = default);
Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
Task UpdateAsync(TEntity entity, CancellationToken ct = default);
Task RemoveAsync(TEntity entity, CancellationToken ct = default);
Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
Task CommitAsync(CancellationToken ct = default);
```

Each mutation runs its advisor pipeline before touching the backing store. When an advisor returns
`Block` or `Handle`, the pipeline stops and the store operation is skipped. `AddRangeAsync` and
`RemoveRangeAsync` fan out to per-entity calls so each entity gets a full pipeline pass — except
LinqToDB's `AddRangeAsync`, which runs the add advisors per entity and then persists the survivors in a
single bulk-copy round trip.

`CommitAsync` persists pending changes and then dispatches `IRepositoryCommittedAdvisor<TEntity>` with
a `CommitChanges<TEntity>` snapshot. See [unit-of-work.md](unit-of-work.md) for transaction and commit
semantics.

## Suppression scopes

Repository `Suppress*()` methods store a marker class in `AdviceContext` and return an `IDisposable` that
restores the prior state on dispose. The convention is a verb method (`SuppressSoftDelete()`) and a state-noun marker
(`SoftDeleteSuppressed`). The advisor checks `ctx.Has<SoftDeleteSuppressed>()` at the top of `AdviseAsync`.

| Entry point                                     | Marker class                 | Advisors bypassed                               |
| ----------------------------------------------- | ---------------------------- | ----------------------------------------------- |
| `repository.SuppressAddValidation()`            | `AddValidationSuppressed`    | `AdviceAddValidation`                           |
| `repository.SuppressUpdateValidation()`         | `UpdateValidationSuppressed` | `AdviceUpdateValidation`                        |
| `repository.SuppressQuerySoftDelete()`          | `QuerySoftDeleteSuppressed`  | `AdviceBuildQuerySoftDelete`                    |
| `repository.SuppressSoftDelete()`               | `SoftDeleteSuppressed`       | `AdviceAddSoftDelete`, `AdviceRemoveSoftDelete` |
| `repository.SuppressTimestamp()`                | `TimestampSuppressed`        | `AdviceAddTimestamp`, `AdviceUpdateTimestamp`   |
| `repository.SuppressQueryOwner()`               | `QueryOwnerSuppressed`       | `AdviceBuildQueryOwner`                         |

Scope a suppression with `using`:

```csharp
using (repository.SuppressQuerySoftDelete())
{
    var tombstone = await repository.FirstOrDefaultAsync<Book>(q => q.Where(b => b.Uid == id), ct);
}
```

The `Schemata.Entity.Owner` and `Schemata.Entity.Cache` packages add further repository scopes as
`IRepository<TEntity>` extension methods — `SuppressOwner()`, `SuppressQueryOwner()`,
`SuppressQueryCache()`, `SuppressQueryCacheEviction()` — when `UseOwner()` or `UseQueryCache()` is
called on the repository builder.

## AdviceContext

Every repository instance holds an `AdviceContext`: a typed property bag keyed by runtime type. It
flows through every advisor call. Advisors and application code share state via `Set<T>`, `TryGet<T>`,
`Get<T>`, `Has<T>`, and `Use<T>` (the scoped variant the `Suppress*` methods build on). The context
also carries the `IServiceProvider`, giving advisors access to any registered service.

## Registration

```csharp
services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
        .UseEntityFrameworkCore<AppDbContext>((sp, opts) => opts.UseSqlite(connectionString))
        .WithUnitOfWork<AppDbContext>()
        .UseOwner()
        .UseQueryCache(o => o.Ttl = TimeSpan.FromMinutes(10));
```

`AddRepository<TEntity, TImplementation>()` is the only registration overload: it registers the
closed-generic repository as a transient `IRepository<TEntity>`, registers all built-in advisors with
`TryAddEnumerable`, and returns a `SchemataRepositoryBuilder` for the provider and opt-in verbs shown
above. Call it once per entity type; multiple implementations can coexist. See
[providers.md](providers.md) for provider setup.

## Extension points

- **Custom advisor** — implement the relevant `IRepository*Advisor<TEntity>` interface and register
  with `TryAddEnumerable`. Pick an `Order` outside the built-in `[100_000_000, 900_000_000]` window.
- **Custom provider** — inherit from `RepositoryBase<TEntity>` and implement its abstract members.
  Satisfying `IRepository<TEntity>` also satisfies the non-generic `IRepository` surface.

## Design rationale

The two-interface split lets infrastructure — unit-of-work coordination, cross-repository advisor
scopes — depend on `IRepository` without binding to a concrete entity type, while entity code uses
`IRepository<TEntity>` for compile-time-safe CRUD. The non-generic surface holds only what does not
need `TEntity`; typed operations stay on the generic interface.

## See also

- [mutation-pipeline.md](mutation-pipeline.md) — add/update/remove advisor chains
- [query-pipeline.md](query-pipeline.md) — build-query/query/result advisor chains
- [providers.md](providers.md) — EF Core and LinqToDB implementations
