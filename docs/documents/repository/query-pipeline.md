# Query Pipeline

Every query method on `IRepository<TEntity>` — `ListAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, `LongCountAsync` — passes through a three-stage advisor pipeline before results are returned. The two raw accessors `AsQueryable` and `AsAsyncEnumerable` bypass the pipeline entirely.

## Where the code lives

| Item | Path |
|---|---|
| `IRepositoryBuildQueryAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryBuildQueryAdvisor.cs` |
| `IRepositoryQueryAdvisor<TEntity,TResult,T>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryQueryAdvisor.cs` |
| `IRepositoryResultAdvisor<TEntity,TResult,T>` | `src/Schemata.Entity.Repository/Advisors/IRepositoryResultAdvisor.cs` |
| `QueryContext<TEntity,TResult,T>` | `src/Schemata.Entity.Repository/QueryContext.cs` |
| `AdviceBuildQuerySoftDelete<TEntity>` | `src/Schemata.Entity.Repository/Advisors/AdviceBuildQuerySoftDelete.cs` |

## Stages

### Stage 1: Build query

```csharp
public interface IRepositoryBuildQueryAdvisor<TEntity>
    : IAdvisor<QueryContainer<TEntity>> where TEntity : class;
```

Build-query advisors run first. They receive a `QueryContainer<TEntity>` that wraps the repository and its initial `IQueryable<TEntity>`. Each advisor calls `container.ApplyModification(q => ...)` to append LINQ operators — filters, ordering, or any other transformation — before the user's predicate is applied.

After all build-query advisors have run, the repository applies the caller-supplied predicate (`Func<IQueryable<TEntity>, IQueryable<TResult>>`). If no predicate was provided, the query falls through to `OfType<TResult>()`.

### Stage 2: Query

```csharp
public interface IRepositoryQueryAdvisor<TEntity, TResult, T>
    : IAdvisor<QueryContext<TEntity, TResult, T>> where TEntity : class;
```

Query advisors run after the query is fully composed but before it is executed against the data store. They receive a `QueryContext` holding the built `IQueryable<TResult>` and a `Result` property.

- **Continue** — execute the query against the database normally.
- **Handle** — skip execution and return `context.Result` as set by the advisor. This is how `AdviceQueryCache` serves cached results without hitting the database.
- **Block** — skip execution and return `default`.

### Stage 3: Result

```csharp
public interface IRepositoryResultAdvisor<TEntity, TResult, T>
    : IAdvisor<QueryContext<TEntity, TResult, T>> where TEntity : class;
```

Result advisors run after the query has executed and `context.Result` has been populated. They can inspect or transform the result before it is returned.

- **Continue** or **Handle** — return `context.Result`.
- **Block** — discard the result and return `default`.

`AdviceResultCache` uses this stage to store freshly fetched results in the cache.

## Execution flow

```text
AsQueryContainer()              create QueryContainer from AsQueryable()
        |
IRepositoryBuildQueryAdvisor    append global filters (soft-delete, owner, etc.)
        |
BuildQuery(predicate)           apply the caller's predicate
        |
IRepositoryQueryAdvisor         short-circuit opportunity (cache hit)
        |
  Execute query                 EF Core / provider materializes the result
        |
IRepositoryResultAdvisor        post-process (cache store, etc.)
        |
  Return result
```

For `ListAsync`, the build-query stage is identical, but the query and result advisor stages are not invoked — the composed queryable is streamed directly as an `IAsyncEnumerable`.

## Built-in build-query advisors

### AdviceBuildQuerySoftDelete

| Property | Value |
|---|---|
| Interface | `IRepositoryBuildQueryAdvisor<TEntity>` |
| Order | 100,000,000 (`SchemataConstants.Orders.Base`) |
| Trait | `ISoftDelete` |
| Suppressed by | `QuerySoftDeleteSuppressed` |

When the entity type implements `ISoftDelete`, appends a filter that excludes rows where `DeleteTime` is non-null:

```csharp
container.ApplyModification(q =>
    q.OfType<ISoftDelete>()
     .Where(e => e.DeleteTime == null)
     .OfType<TEntity>());
```

Entities with a non-null `DeleteTime` are invisible by default. To include them:

```csharp
var all = repository.Once().SuppressQuerySoftDelete().ListAsync<Book>(null);
```

### AdviceBuildQueryOwner

| Property | Value |
|---|---|
| Interface | `IRepositoryBuildQueryAdvisor<TEntity>` |
| Order | 110,000,000 |
| Trait | `IOwnable` |
| Suppressed by | `QueryOwnerSuppressed` |
| Registered by | `UseOwner()` |

Restricts results to entities owned by the current caller. Resolves the owner via `IOwnerResolver<TEntity>` and appends `.Where(e => e.Owner == owner)`. When the resolver returns `null`, behavior is governed by `SchemataOwnerOptions.OnNullOwner`. See [ownership.md](ownership.md).

## QueryContainer and QueryContext

`QueryContainer<TEntity>` carries the build-query stage:

- `Repository` — the `IRepository<TEntity>` that initiated the query.
- `Query` — the current `IQueryable<TEntity>`, modified in place by each advisor via `ApplyModification`.

`QueryContext<TEntity, TResult, T>` carries the query and result stages:

- `Repository` — the `IRepository<TEntity>` that initiated the query.
- `Query` — the fully built `IQueryable<TResult>` (after build-query advisors and the user predicate).
- `Result` — the materialized value; `default` before execution, populated by the provider or by a query advisor that short-circuits.

The `T` type parameter is the scalar return type: `TResult` for `FirstOrDefaultAsync`, `bool` for `AnyAsync`, `int` for `CountAsync`, `long` for `LongCountAsync`.

## Extension points

Register custom build-query advisors for cross-cutting filters:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped(
    typeof(IRepositoryBuildQueryAdvisor<>),
    typeof(MyTenantFilter<>)));
```

Register custom query/result advisors for caching or logging:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped(
    typeof(IRepositoryQueryAdvisor<,,>),
    typeof(MyQueryLogger<,,>)));
```

## See also

- [mutation-pipeline.md](mutation-pipeline.md) — add/update/remove advisor chains
- [caching.md](caching.md) — `AdviceQueryCache` and `AdviceResultCache`
- [ownership.md](ownership.md) — `AdviceBuildQueryOwner` and `IOwnerResolver`
- [entity/traits.md](../entity/traits.md) — `ISoftDelete` and `IOwnable` trait definitions
- [core/advice-pipeline.md](../core/advice-pipeline.md) — `AdviseResult` semantics
