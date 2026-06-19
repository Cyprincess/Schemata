# Repository Overview

`IRepository<TEntity>` is the data-access abstraction in Schemata. It wraps a backing store (Entity
Framework Core, LinqToDB, or a custom provider) behind a uniform API and routes every read and write
through an advisor pipeline that handles timestamps, concurrency stamps, soft-delete, validation,
uniqueness, and canonical-name generation. A non-generic `IRepository` carries the entity-agnostic
surface — `AdviceContext`, `Begin`/`Join`/`CommitAsync`, and the `Suppress*` scopes — so coordination
code that does not know `TEntity` depends on the non-generic interface; `IRepository<TEntity>`
extends it with typed CRUD members.

## Where the code lives

| Item | Path |
| --- | --- |
| `IRepository`, `IRepository<TEntity>` | `src/Schemata.Entity.Repository/IRepository.cs` |
| `RepositoryBase<TEntity>` | `src/Schemata.Entity.Repository/RepositoryBase.cs` |
| `IUnitOfWork`, `IUnitOfWork<TContext>` | `src/Schemata.Entity.Repository/IUnitOfWork.cs` |
| `QueryContainer<TEntity>`, `QueryContext<TEntity,TResult,T>` | `src/Schemata.Entity.Repository/` |
| Built-in advisors | `src/Schemata.Entity.Repository/Advisors/` |
| DI registration | `src/Schemata.Entity.Repository/Extensions/ServiceCollectionExtensions.cs` |

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
ValueTask<long>     EstimateCountAsync<TResult>(
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
`EstimateCountAsync` defaults to `LongCountAsync` and exists for providers that can override it with a
cheaper cardinality estimate.

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

Each `Suppress*()` method stores a marker class in `AdviceContext` and returns an `IDisposable` that
restores the prior state on dispose. The convention is a verb method (`SuppressSoftDelete()`) and a
state-noun marker (`SoftDeleteSuppressed`). The advisor checks `ctx.Has<SoftDeleteSuppressed>()` at the
top of `AdviseAsync`.

| Method | Marker class | Advisors bypassed |
| --- | --- | --- |
| `SuppressAddValidation()` | `AddValidationSuppressed` | `AdviceAddValidation` |
| `SuppressUpdateValidation()` | `UpdateValidationSuppressed` | `AdviceUpdateValidation` |
| `SuppressQuerySoftDelete()` | `QuerySoftDeleteSuppressed` | `AdviceBuildQuerySoftDelete` |
| `SuppressSoftDelete()` | `SoftDeleteSuppressed` | `AdviceAddSoftDelete`, `AdviceRemoveSoftDelete` |
| `SuppressTimestamp()` | `TimestampSuppressed` | `AdviceAddTimestamp`, `AdviceUpdateTimestamp` |

Scope a suppression with `using`:

```csharp
using (repository.SuppressQuerySoftDelete())
{
    var tombstone = await repository.FirstOrDefaultAsync<Book>(q => q.Where(b => b.Uid == id), ct);
}
```

The `Schemata.Entity.Owner` and `Schemata.Entity.Cache` packages add further scopes as
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
services.AddRepository(typeof(EfCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>((sp, opts) => opts.UseSqlite(connectionString))
        .WithUnitOfWork<AppDbContext>()
        .UseOwner()
        .UseQueryCache(o => o.Ttl = TimeSpan.FromMinutes(10));
```

`AddRepository(Type)` validates that the type implements `IRepository<>`, registers it as an
open-generic transient via `TryAddTransient`, and registers all built-in advisors with
`TryAddEnumerable`. The closed-generic overload `AddRepository<TEntity, TImplementation>()` registers a
single entity's repository so multiple implementations can coexist. See [providers.md](providers.md)
for provider setup.

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
