# Repository Overview

`IRepository<TEntity>` is the data-access abstraction in Schemata. It wraps a backing store (Entity
Framework Core, LinqToDB, or a custom provider) behind a uniform API and routes every read and write
through an advisor pipeline that handles timestamps, concurrency stamps, soft-delete, validation,
uniqueness, and canonical-name generation. A non-generic `IRepository` carries the entity-agnostic
surface — `AdviceContext`, `Begin`/`Join`/`CommitAsync` — so coordination
code that does not know `TEntity` depends on the non-generic interface; `IRepository<TEntity>`
extends it with typed CRUD members and the per-operation `QueryOptions`/`MutationOptions`
parameters.

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
a custom `IEfCoreCountEstimator<TContext>`. The built-in estimator executes parameterized plan reads
through EF reader interception and SHOWPLAN mode commands through nonquery interception. Policies
receive the actual plan SQL and may reject it. LinqToDB selects estimation by its registered provider.

The plan backends are PostgreSQL `EXPLAIN (FORMAT JSON)`, MySQL `EXPLAIN FORMAT=JSON`, and SQL Server
`SHOWPLAN_XML`. These request optimizer plans without executing the SELECT for a count. LinqToDB also
supports SQLite statistics for a restricted unfiltered table query and returns `null` for MariaDB or
unrecognized provider names. See [Repository Providers](providers.md) for activation, query-shape limits,
and connection requirements.

## Mutation API

```csharp
Task<MutationResult> AddAsync(TEntity entity, CancellationToken ct = default);
Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
Task<MutationResult> UpdateAsync(TEntity entity, CancellationToken ct = default);
Task<MutationResult> RemoveAsync(TEntity entity, CancellationToken ct = default);
Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
Task CommitAsync(CancellationToken ct = default);
```

Each single-entity mutation runs its advisor pipeline before touching the backing store and returns a
`MutationResult`: `Applied` when this call staged or executed a write, `NoWrite` when an advisor
blocked or handled it without staging one. `Block` or `Handle` stops the pipeline and skips the
store operation. `AddRangeAsync` and `RemoveRangeAsync` fan out to per-entity calls so each entity
gets a full pipeline pass — except LinqToDB's `AddRangeAsync`, which runs the add advisors per
entity and then persists the survivors in a single bulk-copy round trip.

`CommitAsync` persists pending changes; the unit of work then runs two ordered segments — the
repository type-level `IRepositoryCommittedAdvisor<TEntity>` notification (skippable when nothing
staged) followed by the `IResourceMutationCommittedAdvisor<TEntity>` and domain sinks enlisted by
resource mutations and application code. See [unit-of-work.md](unit-of-work.md) for transaction and
commit semantics.

## Per-operation options

Queries take `QueryOptions` and mutations take `MutationOptions` on the call that needs the
override. Both apply to that one operation, so nested and parallel reads and writes never inherit
them. `IgnoreCacheEviction` is the exception: it records a commit-scoped request for the
repository's current unit of work instead of changing the operation itself, and both options
records declare it.

| Option                | Type               | Advisors bypassed                                                 |
| --------------------- | ------------------ | ----------------------------------------------------------------- |
| `IgnoreValidation`    | `MutationOptions`  | `AdviceAddValidation`, `AdviceUpdateValidation`                   |
| `IgnoreUniqueness`    | `MutationOptions`  | `AdviceAddUniqueness`                                             |
| `IgnoreSoftDelete`    | both               | query: `AdviceBuildQuerySoftDelete`; mutation: `AdviceAddSoftDelete`, `AdviceRemoveSoftDelete` |
| `IgnoreOwner`         | `QueryOptions`     | `AdviceBuildQueryOwner`                                           |
| `IgnoreCache`         | `QueryOptions`     | `AdviceQueryCache`, `AdviceResultCache`                           |
| `IgnoreCacheEviction` | both records       | `AdviceCommittedEvictCache` for the next commit                   |

```csharp
var tombstone = await repository.FirstOrDefaultAsync<Book>(
    q => q.Where(b => b.Uid == id), ct, new QueryOptions(IgnoreSoftDelete: true));
```

## AdviceContext

Every repository instance exposes an `AdviceContext` bound to the repository's `IServiceProvider` and
passes it to every advisor call, giving advisors access to any registered service. The context holds
no state; advisor payloads travel as explicit parameters.

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
