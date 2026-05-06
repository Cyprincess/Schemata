# Caching

The `Schemata.Entity.Cache` package adds distributed query caching and automatic eviction to the repository layer. It ships four advisors: two that participate in the query pipeline (intercepting before execution to serve cached results, and after execution to store results), and two mutation advisors that evict stale cache entries on update and remove.

Caching is **opt-in**. Enable it by calling `UseQueryCache` on the repository builder.

## Package

| Package                 | Dependency                                                          | Targets                                                 |
| ----------------------- | ------------------------------------------------------------------- | ------------------------------------------------------- |
| `Schemata.Entity.Cache` | `Schemata.Entity.Repository`, `Schemata.Caching.Skeleton`           | `netstandard2.0`, `netstandard2.1`, `net8.0`, `net10.0` |

The `Schemata.Entity.Cache` package itself does not depend on any concrete cache backend. It relies on `ICacheProvider` from `Schemata.Caching.Skeleton`. You must separately register a provider — see [Cache Overview](../cache/overview.md).

## Advisors

### AdviceQueryCache

`AdviceQueryCache<TEntity, TResult, T>` implements `IRepositoryQueryAdvisor<TEntity, TResult, T>`. It runs **before** the query is executed against the data store.

**Behavior:**

1. If the `AdviceContext` contains a `QueryCacheSuppressed` flag, the advisor returns `AdviseResult.Continue` and does nothing.
2. It generates a cache key from the query expression via `context.ToCacheKey()`. If the key is null or whitespace, it continues.
3. It calls `ICacheProvider.GetAsync(key)` to look up the serialized result. On a cache miss (null bytes), it continues.
4. On a cache hit, it deserializes the byte array via `JsonSerializer.Deserialize<T>` and assigns it to `context.Result`, then returns `AdviseResult.Handle`. This short-circuits database execution entirely.

**Order:** `SchemataConstants.Orders.Base` (100,000,000).

### AdviceResultCache

`AdviceResultCache<TEntity, TResult, T>` implements `IRepositoryResultAdvisor<TEntity, TResult, T>`. It runs **after** the query has been executed and a result is available.

**Behavior:**

1. If the `AdviceContext` contains a `QueryCacheSuppressed` flag, the advisor returns `AdviseResult.Continue` and does nothing.
2. If `context.Result` is null, it continues without caching.
3. It generates a cache key from the query expression. If the key is null or whitespace, it continues.
4. It serializes the result via `JsonSerializer.SerializeToUtf8Bytes` and stores it via `ICacheProvider.SetAsync` with a sliding expiration equal to `SchemataQueryCacheOptions.Ttl` (default 5 minutes).
5. If the result is of type `TEntity`, it additionally records the cache key in the **reverse index** — a set mapping `(entityType, primaryKey)` to all cache keys that contain that entity. This enables precise eviction on update and remove.

**Order:** `SchemataConstants.Orders.Base` (100,000,000).

### AdviceUpdateEvictCache

`AdviceUpdateEvictCache<TEntity>` implements `IRepositoryUpdateAdvisor<TEntity>`. It runs during the update mutation pipeline and evicts all cached query results that include the entity being updated.

**Behavior:**

1. If `SchemataQueryCacheOptions.EvictionEnabled` is `false`, or if the `AdviceContext` contains `QueryCacheEvictionSuppressed`, the advisor skips eviction.
2. It builds the reverse-index key for `(typeof(TEntity), entity)`.
3. It reads all member cache keys from the reverse index set via `ICacheProvider.CollectionMembersAsync`.
4. It calls `ICacheProvider.RemoveAsync` for each member key to delete the stale cached result.
5. It clears the reverse-index set via `ICacheProvider.CollectionClearAsync`.

This ensures that after an update, the next query for the affected entity produces a fresh result from the database, which is then re-cached.

**Order:** `SchemataConstants.Orders.Max` (900,000,000) — runs last, after all other update advisors.

### AdviceRemoveEvictCache

`AdviceRemoveEvictCache<TEntity>` implements `IRepositoryRemoveAdvisor<TEntity>`. It mirrors `AdviceUpdateEvictCache` for remove operations. On soft-delete (where `AdviceRemoveSoftDelete` handles the remove), the eviction still fires when the remove advisor pipeline runs.

**Order:** `SchemataConstants.Orders.Max` (900,000,000).

## Suppression flags

| Flag                          | Purpose                                                    | Set via                             |
| ----------------------------- | ---------------------------------------------------------- | ----------------------------------- |
| `QueryCacheSuppressed`        | Skips both query cache and result cache for a repository   | `repository.SuppressQueryCache()`   |
| `QueryCacheEvictionSuppressed` | Skips auto-eviction on update and remove                   | `repository.SuppressQueryCacheEviction()` |

Both suppression methods return the repository instance for fluent chaining and can be scoped via `Once()`:

```csharp
// Bypass cache for a single query
var fresh = await repository.Once()
    .SuppressQueryCache()
    .FirstOrDefaultAsync(q => q.Where(e => e.Id == id), ct);

// Update without triggering eviction
await repository.Once()
    .SuppressQueryCacheEviction()
    .UpdateAsync(entity, ct);
```

## Reverse index

The reverse index is a per-entity set in the cache that maps `(entityType, primaryKey)` to the collection of cache keys for query results containing that entity. It is the mechanism that enables precise eviction.

**Key format:** `{entityType.FullName}\x1e{primaryKey}` hashed via `CityHash128` with the `Keys.Entity` domain.

For single-column keys, the primary key is the `ToString()` of the key property value. For composite keys, values are joined with `\x1f` (ASCII Unit Separator).

**Write path:** `AdviceResultCache` adds the query's cache key to the set when the result is a single `TEntity` (not a projection/scalar/aggregate). Aggregate queries (`Any`, `Count`, `LongCount`) and projections (`Select` into a DTO) are cached but NOT reverse-indexed, since they do not correspond to a single entity.

**Eviction path:** `AdviceUpdateEvictCache` and `AdviceRemoveEvictCache` enumerate the set members and delete each one, then clear the set.

## How caching fits into the query pipeline

```
BuildQuery advisors
    |
    v
Query advisors (AdviceQueryCache runs here)
    |-- cache hit --> deserialize → context.Result = cached → Handle (skip DB)
    |-- cache miss --> continue
    v
Execute query against database
    |
    v
Result advisors (AdviceResultCache runs here)
    |-- serialize → ICacheProvider.SetAsync
    |-- if TEntity → add key to reverse index
    v
Return result
```

When `AdviceQueryCache` finds a cached value and returns `AdviseResult.Handle`, the repository returns that value immediately. The database query and all result advisors are skipped.

## Cache key generation

Cache keys for queries are derived from the LINQ expression tree:

1. The `QueryContextExtensions.ToCacheKey` method serializes the query's `Expression` into a deterministic string via the `Stringizing` expression visitor. This visitor walks the expression tree and produces a stable representation of lambdas, binary operations, method calls, member accesses, constants, and unary operations. Parameters are normalized (`_p0`, `_p1`), `IFormattable` values use `InvariantCulture`, and method calls include `:arity` suffixes to disambiguate overloads.
2. The return type name (`typeof(T).Name`) is appended, separated by `\x1e`.
3. The combined string is hashed with `CityHash128` and prefixed with `{SchemataGuid}\x1e{Keys.Entity}\x1e` via the `ToCacheKey` extension method.

Two queries that produce the same LINQ expression tree and target the same return type will share a cache key.

## Options

### SchemataQueryCacheOptions

| Property           | Type      | Default               | Description                                                    |
| ------------------ | --------- | --------------------- | -------------------------------------------------------------- |
| `Ttl`              | `TimeSpan`| 5 minutes             | Sliding expiration for cached query results and reverse index entries |
| `EvictionEnabled`  | `bool`    | `true`                | When `false`, update and remove advisors skip cache eviction. Query and result advisors remain active |

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
    .UseEntityFrameworkCore<AppDbContext>(configure)
    .UseQueryCache(options => {
        options.Ttl = TimeSpan.FromMinutes(10);
        options.EvictionEnabled = false; // rely on TTL only
    });
```

## Configuration

### Enabling query caching

Call `UseQueryCache` on the `SchemataRepositoryBuilder` returned by `AddRepository`:

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
    .UseEntityFrameworkCore<AppDbContext>(configure)
    .UseQueryCache();
```

`UseQueryCache`:

1. Registers `SchemataQueryCacheOptions` (with optional configuration callback).
2. Registers all four cache advisors as `Scoped` open-generic services:
   - `AdviceQueryCache<,,>` as `IRepositoryQueryAdvisor<,,>`
   - `AdviceResultCache<,,>` as `IRepositoryResultAdvisor<,,>`
   - `AdviceUpdateEvictCache<>` as `IRepositoryUpdateAdvisor<>`
   - `AdviceRemoveEvictCache<>` as `IRepositoryRemoveAdvisor<>`

A concrete `ICacheProvider` implementation must be registered separately in DI. The advisors resolve `ICacheProvider` from the container.

### Registering a cache provider

**In-memory / distributed (single-process):**

```csharp
// Uses any IDistributedCache (in-memory, SQL Server, etc.)
services.AddDistributedMemoryCache();
services.AddDistributedCache();
```

**Redis (multi-process, cluster-safe):**

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddRedisCache();
```

See [Cache Providers](../cache/providers.md) for a detailed comparison of backend options and their characteristics.
