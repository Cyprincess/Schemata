# Query Pipeline

Every query method on `IRepository<TEntity>` -- `ListAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, `LongCountAsync` -- passes through a three-stage advisor pipeline before results are returned. The two raw accessors `AsQueryable` and `AsAsyncEnumerable` bypass the pipeline entirely.

## Stages

### 1. Build Query -- `IRepositoryBuildQueryAdvisor<TEntity>`

```csharp
public interface IRepositoryBuildQueryAdvisor<TEntity>
    : IAdvisor<QueryContainer<TEntity>> where TEntity : class;
```

Build-query advisors run first. They receive a `QueryContainer<TEntity>` that wraps the repository and its initial `IQueryable<TEntity>`. Each advisor can call `container.ApplyModification(q => ...)` to append LINQ operators -- filters, ordering, or any other transformation -- to the query before the user's predicate is applied.

After all build-query advisors have run, the repository applies the caller-supplied predicate (the `Func<IQueryable<TEntity>, IQueryable<TResult>>` parameter) on top of the modified queryable. If no predicate was provided, the query falls through to `OfType<TResult>()`.

### 2. Query -- `IRepositoryQueryAdvisor<TEntity, TResult, T>`

```csharp
public interface IRepositoryQueryAdvisor<TEntity, TResult, T>
    : IAdvisor<QueryContext<TEntity, TResult, T>> where TEntity : class;
```

Query advisors run after the query is fully composed but **before** it is executed against the data store. They receive a `QueryContext` that holds the built `IQueryable<TResult>` and a `Result` property.

The return value controls what happens next:

- **Continue** -- execute the query against the database normally.
- **Handle** -- skip execution and return `context.Result` as set by the advisor. This is the mechanism a cache advisor uses to serve cached results without hitting the database.
- **Block** -- skip execution and return `default`.

### 3. Result -- `IRepositoryResultAdvisor<TEntity, TResult, T>`

```csharp
public interface IRepositoryResultAdvisor<TEntity, TResult, T>
    : IAdvisor<QueryContext<TEntity, TResult, T>> where TEntity : class;
```

Result advisors run after the query has executed and `context.Result` has been populated. They can inspect or transform the result before it is returned to the caller.

- **Continue** or **Handle** -- return `context.Result`.
- **Block** -- discard the result and return `default`.

This stage is used by the cache advisor to store freshly fetched results in the cache.

## Execution Flow

The complete lifecycle for a scalar query (e.g. `FirstOrDefaultAsync`) is:

```
AsQueryContainer()              create QueryContainer from AsQueryable()
        |
IRepositoryBuildQueryAdvisor    modify the IQueryable (global filters, etc.)
        |
BuildQuery(predicate)           apply the caller's predicate
        |
IRepositoryQueryAdvisor         short-circuit opportunity (cache hit, etc.)
        |
  Execute query                 EF Core / provider materializes the result
        |
IRepositoryResultAdvisor        post-process the result (cache store, etc.)
        |
  Return result
```

For `ListAsync`, the build-query stage is identical, but the query and result advisor stages are not invoked -- the composed queryable is streamed directly as an `IAsyncEnumerable`.

## Built-in Query Advisors

### AdviceBuildQuerySoftDelete

| Property      | Value                                         |
| ------------- | --------------------------------------------- |
| Interface     | `IRepositoryBuildQueryAdvisor<TEntity>`       |
| Order         | 100,000,000 (`SchemataConstants.Orders.Base`) |
| Trait         | `ISoftDelete`                                 |
| Suppressed by | `SuppressQuerySoftDelete`                     |

This is the only built-in build-query advisor. When the entity type implements `ISoftDelete`, it appends a filter that excludes rows where `DeleteTime` is non-null:

```csharp
container.ApplyModification(q =>
    q.OfType<ISoftDelete>()
     .Where(e => e.DeleteTime == null)
     .OfType<TEntity>());
```

The filter applies to every query method that uses the pipeline: `ListAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, and `LongCountAsync`. Entities with a non-null `DeleteTime` are invisible by default.

To include soft-deleted entities in a query, suppress the filter:

```csharp
var all = repository.Once().SuppressQuerySoftDelete().ListAsync<Product>(null);
```

The framework does not ship built-in `IRepositoryQueryAdvisor` or `IRepositoryResultAdvisor` implementations. These extension points exist for application-level concerns such as caching, logging, or access-control checks that need to run around query execution.

## QueryContainer and QueryContext

`QueryContainer<TEntity>` is the carrier for the build-query stage. It holds:

- `Repository` -- the `IRepository<TEntity>` that initiated the query.
- `Query` -- the current `IQueryable<TEntity>`, mutated in place by each advisor via `ApplyModification`.

`QueryContext<TEntity, TResult, T>` is the carrier for the query and result stages. It holds:

- `Repository` -- the `IRepository<TEntity>` that initiated the query.
- `Query` -- the fully built `IQueryable<TResult>` (after build-query advisors and the user predicate).
- `Result` -- the materialized value. `null`/`default` before execution; populated by the provider after execution or by a query advisor that short-circuits.

The `T` type parameter represents the scalar return type of the operation. For `FirstOrDefaultAsync<TResult>` it is `TResult`; for `AnyAsync` it is `bool`; for `CountAsync` it is `int`; for `LongCountAsync` it is `long`.

## Custom Query Advisors

Register custom advisors in DI just like mutation advisors:

```csharp
services.AddScoped(
    typeof(IRepositoryBuildQueryAdvisor<>),
    typeof(MyTenantFilter<>));
```

A common use case is multi-tenancy. A build-query advisor can inspect the current user's tenant from `AdviceContext.ServiceProvider`, then append a `.Where(e => e.TenantId == currentTenant)` filter to every query automatically.
