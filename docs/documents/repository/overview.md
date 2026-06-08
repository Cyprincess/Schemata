# Repository Overview

`IRepository<TEntity>` is the primary data-access abstraction in Schemata. It wraps a backing store (Entity Framework Core, LinqToDB, or any custom provider) behind a uniform API and routes every read and write through an advisor pipeline that handles timestamps, concurrency stamps, soft-delete, validation, and canonical-name generation automatically.

A non-generic `IRepository` mirror exists for type-erased scenarios. The Resource layer uses it to operate on entities whose concrete type is known only at runtime. Both interfaces share the same underlying implementation: `RepositoryBase<TEntity>` implements both and delegates the non-generic calls to the generic ones after a type check.

## Where the code lives

| Item | Path |
|---|---|
| `IRepository<TEntity>` | `src/Schemata.Entity.Repository/IRepository`1.cs` |
| `IRepository` (non-generic) | `src/Schemata.Entity.Repository/IRepository.cs` |
| `RepositoryBase<TEntity>` | `src/Schemata.Entity.Repository/RepositoryBase.cs` |
| `IUnitOfWork` | `src/Schemata.Entity.Repository/IUnitOfWork.cs` |
| `QueryContext<TEntity,TResult,T>` | `src/Schemata.Entity.Repository/QueryContext.cs` |
| Built-in advisors | `src/Schemata.Entity.Repository/Advisors/` |
| DI registration | `src/Schemata.Entity.Repository/Extensions/ServiceCollectionExtensions.cs` |

## Query API

```csharp
// Raw access — bypasses the advisor pipeline
IAsyncEnumerable<TEntity> AsAsyncEnumerable();
IQueryable<TEntity>       AsQueryable();

// Advisor-gated queries
IAsyncEnumerable<TResult> ListAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);

ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);

ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);

ValueTask<bool> AnyAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);

ValueTask<int>  CountAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);

ValueTask<long> LongCountAsync<TResult>(
    Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default);

// Key-based lookup (delegates to SingleOrDefaultAsync)
ValueTask<TEntity?> GetAsync(TEntity entity, CancellationToken ct = default);
ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default);
```

`AsAsyncEnumerable` and `AsQueryable` return the raw data set without running the advisor pipeline. Every other query method runs build-query advisors first (for example, the soft-delete filter) before executing.

The `predicate` parameter is a `Func<IQueryable<TEntity>, IQueryable<TResult>>`, a query transformation rather than a simple expression tree. This lets you chain `Where`, `Select`, `OrderBy`, `Take`, and any other LINQ operator in a single lambda:

```csharp
var page = repository.ListAsync<BookDto>(q =>
    q.Where(b => b.Price > 10)
     .OrderBy(b => b.Name)
     .Select(b => new BookDto(b.Uid, b.Name))
     .Skip(20)
     .Take(10));
```

## Mutation API

```csharp
Task AddAsync(TEntity entity, CancellationToken ct = default);
Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
Task UpdateAsync(TEntity entity, CancellationToken ct = default);
Task RemoveAsync(TEntity entity, CancellationToken ct = default);
Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
ValueTask<int> CommitAsync(CancellationToken ct = default);
```

Each mutation runs the corresponding advisor pipeline before touching the backing store. If any advisor returns `Block` or `Handle`, the mutation short-circuits and the store operation is skipped. `AddRangeAsync` and `RemoveRangeAsync` fan out to individual calls so every entity gets its own full pipeline pass.

`CommitAsync` flushes pending changes to the database and then dispatches `IRepositoryCommittedAdvisor<TEntity>` with a `CommitChanges<TEntity>` snapshot. See [unit-of-work.md](unit-of-work.md) for transaction semantics.

## Once() and Suppress*()

`Once()` creates a new repository instance with a fresh `AdviceContext` by calling `ActivatorUtilities.CreateInstance(ServiceProvider, GetType())`. The new instance has its own `AdviceContext` and provider context.

```csharp
// Suppress for one call only — the original instance is unaffected
await repository.Once().SuppressQuerySoftDelete().FirstOrDefaultAsync<Book>(
    q => q.Where(b => b.Uid == id));
```

`ResourceOperationHandler.FindByNameAsync` uses exactly this pattern: `_repository.Once().SuppressQuerySoftDelete()` so name lookups always see tombstoned rows.

Every `Suppress*()` method stores a marker class in `AdviceContext` and returns `this` for fluent chaining. The naming convention is: the method is a verb (`SuppressSoftDelete()`); the marker is a state noun (`SoftDeleteSuppressed`).

| Method | Marker class | Advisors bypassed |
|---|---|---|
| `SuppressAddValidation()` | `AddValidationSuppressed` | `AdviceAddValidation` |
| `SuppressUpdateValidation()` | `UpdateValidationSuppressed` | `AdviceUpdateValidation` |
| `SuppressConcurrency()` | `ConcurrencySuppressed` | `AdviceAddConcurrency`, `AdviceUpdateConcurrency` |
| `SuppressQuerySoftDelete()` | `QuerySoftDeleteSuppressed` | `AdviceBuildQuerySoftDelete` |
| `SuppressSoftDelete()` | `SoftDeleteSuppressed` | `AdviceAddSoftDelete`, `AdviceRemoveSoftDelete` |
| `SuppressTimestamp()` | `TimestampSuppressed` | `AdviceAddTimestamp`, `AdviceUpdateTimestamp` |

The `Schemata.Entity.Owner` and `Schemata.Entity.Cache` packages add further suppression methods (`OwnerSuppressed`, `QueryOwnerSuppressed`, `QueryCacheSuppressed`, `QueryCacheEvictionSuppressed`) when `UseOwner()` or `UseQueryCache()` is called on the repository builder.

## AdviceContext

Every repository instance holds an `AdviceContext` property. It is a typed property bag that flows through every advisor call. Advisors and application code can store arbitrary state via `Set<T>`, `TryGet<T>`, `Get<T>`, and `Has<T>`. The context also carries the `IServiceProvider`, giving advisors access to any DI-registered service.

## Registration

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .WithUnitOfWork<MyDbContext>()
        .UseOwner()
        .UseQueryCache(o => o.Ttl = TimeSpan.FromMinutes(10));
```

`AddRepository` validates that the implementation type implements both `IRepository` and `IRepository<>`, registers it as open-generic transient, and registers all built-in advisors via `TryAddEnumerable`. See [providers.md](providers.md) for provider-specific setup.

## Extension points

- **Custom advisors**: implement `IRepositoryAddAdvisor<TEntity>`, `IRepositoryUpdateAdvisor<TEntity>`, `IRepositoryRemoveAdvisor<TEntity>`, or `IRepositoryBuildQueryAdvisor<TEntity>` and register with `TryAddEnumerable`. Pick an `Order` outside `[100_000_000, 900_000_000]`.
- **Custom providers**: inherit from `RepositoryBase<TEntity>` and implement the abstract members. The non-generic `IRepository` surface comes for free.

## Design motivation

The two-interface design (`IRepository` + `IRepository<TEntity>`) lets the Resource layer hold a single `IRepository` reference and dispatch to any entity type at runtime without generics. `RepositoryBase<TEntity>` bridges the gap by casting through `Predicate.Cast<T, TEntity>` so trait-typed predicates (e.g., `Expression<Func<ISoftDelete, bool>>`) work across the type boundary.

## See also

- [mutation-pipeline.md](mutation-pipeline.md) — add/update/remove advisor chains
- [query-pipeline.md](query-pipeline.md) — build-query/query/result advisor chains
- [unit-of-work.md](unit-of-work.md) — explicit enlistment, `CommitAsync`, committed advisors
- [providers.md](providers.md) — EF Core and LinqToDB implementations
- [ownership.md](ownership.md) — `UseOwner()` and `IOwnerResolver`
- [caching.md](caching.md) — `UseQueryCache()` and cache eviction
- [entity/traits.md](../entity/traits.md) — trait interfaces and their advisors
