# Caching

The `Schemata.Entity.Cache` package adds in-memory query caching to the repository layer. It ships two advisors that participate in the query pipeline -- one that intercepts queries before execution and returns cached results, and one that stores results after execution -- plus a suppression mechanism for bypassing the cache on a per-call basis.

Caching is **opt-in**. The advisors are not registered by `AddRepository`. You enable them by calling `UseQueryCache` on the repository builder.

## Package

| Package                 | Dependency                                                          | Targets                                                 |
| ----------------------- | ------------------------------------------------------------------- | ------------------------------------------------------- |
| `Schemata.Entity.Cache` | `Schemata.Entity.Repository`, `Microsoft.Extensions.Caching.Memory` | `netstandard2.0`, `netstandard2.1`, `net8.0`, `net10.0` |

## Advisors

### AdviceQueryCache

`AdviceQueryCache<TEntity, TResult, T>` implements `IRepositoryQueryAdvisor<TEntity, TResult, T>`. It runs **before** the query is executed against the data store.

**Behavior:**

1. If the `AdviceContext` contains a `SuppressQueryCache` flag, the advisor returns `AdviseResult.Continue` and does nothing.
2. It generates a cache key from the query expression by calling `context.ToCacheKey()`. If the key is null or whitespace, it continues.
3. It looks up the key in `IMemoryCache`. On a cache miss, it continues.
4. On a cache hit, it sets `context.Result` to the cached value and returns `AdviseResult.Handle`, which short-circuits database execution entirely.

**Order:** `SchemataConstants.Orders.Base` (100,000,000).

### AdviceResultCache

`AdviceResultCache<TEntity, TResult, T>` implements `IRepositoryResultAdvisor<TEntity, TResult, T>`. It runs **after** the query has been executed and a result is available.

**Behavior:**

1. If the `AdviceContext` contains a `SuppressQueryCache` flag, the advisor returns `AdviseResult.Continue` and does nothing.
2. If `context.Result` is null, it continues without caching.
3. It generates a cache key from the query expression. If the key is null or whitespace, it continues.
4. It stores the result in `IMemoryCache` with the following settings:
   - **Priority:** `CacheItemPriority.Normal`
   - **Sliding expiration:** 5 minutes
5. It returns `AdviseResult.Continue`, allowing the result to flow back to the caller unchanged.

**Order:** `SchemataConstants.Orders.Base` (100,000,000).

### SuppressQueryCache

`SuppressQueryCache` is an internal marker class (not an advisor). When present in the `AdviceContext`, both `AdviceQueryCache` and `AdviceResultCache` skip their logic entirely. This means the query always hits the database and the result is not stored in the cache.

## How caching fits into the query pipeline

Every query method on a repository (`FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, `LongCountAsync`) follows the same two-phase advisor pattern:

```
BuildQuery advisors
    |
    v
Query advisors (AdviceQueryCache runs here)
    |-- cache hit --> return cached result (Handle)
    |-- cache miss --> continue
    v
Execute query against database
    |
    v
Result advisors (AdviceResultCache runs here)
    |-- store result in cache
    v
Return result
```

When `AdviceQueryCache` finds a cached value and returns `AdviseResult.Handle`, the repository returns that value immediately. The database query and all result advisors are skipped.

When there is no cache hit, the query executes normally. After execution, `AdviceResultCache` stores the non-null result in the cache so that subsequent identical queries can be served from memory.

## Cache key generation

Cache keys are derived from the LINQ expression tree of the query. The `QueryContextExtensions.ToCacheKey` method:

1. Serializes the query's `Expression` into a deterministic string using the `Stringizing` expression visitor. This visitor walks the expression tree and produces a human-readable string representation of lambdas, binary operations, method calls, member accesses, constants, and unary operations.
2. Appends the return type name (`typeof(T).Name`) separated by a record separator character (`\x1e`).
3. Hashes the combined string using CityHash64 and prepends the Schemata framework GUID, producing a final key in the form `{SchemataGuid}\x1e{hash}`.

Two queries that produce the same LINQ expression tree and target the same return type will share a cache key.

## Configuration

### Enabling query caching

Call `UseQueryCache` on the `SchemataRepositoryBuilder` returned by `AddRepository`:

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
    .UseEntityFrameworkCore<AppDbContext>(configure)
    .UseQueryCache();
```

`UseQueryCache` does two things:

1. Calls `services.AddMemoryCache()` to register the default `IMemoryCache` implementation if not already registered.
2. Registers `AdviceQueryCache<,,>` as an `IRepositoryQueryAdvisor<,,>` and `AdviceResultCache<,,>` as an `IRepositoryResultAdvisor<,,>`, both with `Scoped` lifetime.

### Suppressing the cache

To bypass caching for a single repository instance, call the `SuppressQueryCache` extension method on the repository before executing a query:

```csharp
// On IRepository<TEntity>
var fresh = await repository.SuppressQueryCache()
    .FirstOrDefaultAsync(q => q.Where(e => e.Id == id), ct);

// On IRepository (non-generic)
repository.SuppressQueryCache();
```

This sets the `SuppressQueryCache` marker in the repository's `AdviceContext`. Both cache advisors check for this marker at the start of their `AdviseAsync` method and skip all caching logic when it is present.

The suppression is scoped to the repository instance. Because repositories are registered with `Scoped` lifetime, the flag persists for the duration of the current request. To get a clean instance without the flag, call `repository.Once()` which creates a new repository instance via `ActivatorUtilities.CreateInstance`.
