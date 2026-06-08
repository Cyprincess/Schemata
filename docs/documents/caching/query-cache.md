# Query Cache

The `Schemata.Entity.Cache` package adds transparent query caching and automatic eviction to the repository layer. Results are serialized to JSON and stored in `ICacheProvider`. Cache keys are derived deterministically from the LINQ expression tree. Eviction runs after a successful repository commit through the committed advisor pipeline.

## Where the code lives

| Item | Path |
|---|---|
| `AdviceQueryCache<,,>` | `src/Schemata.Entity.Cache/Advisors/AdviceQueryCache.cs` |
| `AdviceResultCache<,,>` | `src/Schemata.Entity.Cache/Advisors/AdviceResultCache.cs` |
| `AdviceCommittedEvictCache<>` | `src/Schemata.Entity.Cache/Advisors/AdviceCommittedEvictCache.cs` |
| `ReverseIndex` | `src/Schemata.Entity.Cache/ReverseIndex.cs` |
| `Stringizing` | `src/Schemata.Entity.Cache/Stringizing.cs` |
| `PartialEvaluator` | `src/Schemata.Entity.Cache/PartialEvaluator.cs` |
| `SchemataQueryCacheOptions` | `src/Schemata.Entity.Cache/SchemataQueryCacheOptions.cs` |
| `UseQueryCache` extension | `src/Schemata.Entity.Cache/Extensions/SchemataRepositoryBuilderExtensions.cs` |

## The cache advisors

### AdviceQueryCache

**Interface:** `IRepositoryQueryAdvisor<TEntity, TResult, T>`
**Order:** 100,000,000 (`SchemataConstants.Orders.Base`)

Runs before the query executes against the database. On a cache hit, sets `context.Result` and returns `AdviseResult.Handle`, short-circuiting database execution entirely.

**Steps:**

1. If `QueryCacheSuppressed` is in the advice context, returns `Continue`.
2. Calls `context.ToCacheKey()` to derive the cache key from the query expression. If the key is null or whitespace, returns `Continue`.
3. Calls `ICacheProvider.GetAsync(key)`. On a cache miss (null bytes), returns `Continue`.
4. Deserializes the bytes via `JsonSerializer.Deserialize<T>`. If deserialization returns null, returns `Continue`.
5. Sets `context.Result = result` and returns `Handle`.

### AdviceResultCache

**Interface:** `IRepositoryResultAdvisor<TEntity, TResult, T>`
**Order:** 100,000,000 (`SchemataConstants.Orders.Base`)

Runs after the query executes and `context.Result` is populated. Stores the result in the cache and, for single-entity results, records the cache key in the reverse index.

**Steps:**

1. If `QueryCacheSuppressed` is in the advice context, returns `Continue`.
2. If `context.Result` is null, returns `Continue`.
3. Calls `context.ToCacheKey()`. If null or whitespace, returns `Continue`.
4. Serializes `context.Result` via `JsonSerializer.SerializeToUtf8Bytes` and calls `ICacheProvider.SetAsync` with `SlidingExpiration = SchemataQueryCacheOptions.Ttl` (default 5 minutes).
5. If `context.Result is TEntity entity`, calls `ReverseIndex.BuildKey(typeof(TEntity), entity)` and adds the query cache key to the reverse index set via `ICacheProvider.CollectionAddAsync`.
6. Returns `Continue`.

Only single-entity results (where `T == TEntity`) are reverse-indexed. Aggregate queries (`AnyAsync`, `CountAsync`, `LongCountAsync`) and projections (`Select` into a DTO) are cached but not reverse-indexed.

### AdviceCommittedEvictCache

**Interface:** `IRepositoryCommittedAdvisor<TEntity>`
**Order:** 900,000,000 (`SchemataConstants.Orders.Max`)

Runs after a standalone repository commit or unit-of-work commit succeeds. It receives a `CommitChanges<TEntity>` snapshot and evicts cache entries for updated and removed entities. Added entities are ignored.

**Steps:**

1. If `SchemataQueryCacheOptions.EvictionEnabled` is `false`, or if `QueryCacheEvictionSuppressed` is in the advice context, returns `Continue`.
2. Iterates `changes.Updated` and `changes.Removed`.
3. For each entity, calls `ReverseIndex.BuildKey(typeof(TEntity), entity)`.
4. Reads all cache keys from the reverse index set via `ICacheProvider.CollectionMembersAsync`, calls `ICacheProvider.RemoveAsync` for each, then clears the reverse index set via `ICacheProvider.CollectionClearAsync`.
5. Returns `Continue`.

## Reverse index

The reverse index maps `(entity type, primary key)` to the set of cache keys that contain a result for that entity. It enables precise eviction: when an entity is updated or removed, only the cache entries that contain that specific entity are evicted.

**Key format:** `{entityType.FullName}\x1e{primaryKey}` passed through `ToCacheKey(SchemataConstants.Keys.Entity)`, which hashes the string and prefixes it with the Schemata domain marker.

For single-column primary keys, the key value is formatted via `IFormattable.ToString(null, InvariantCulture)` or `ToString()`. For composite keys, values are joined with `\x1f` (ASCII Unit Separator).

`ReverseIndex.BuildKey` returns `null` when no key properties can be resolved (no `[PrimaryKey]` attribute and no `IIdentifier.Uid` property), in which case the result is cached but not reverse-indexed and will only expire via TTL.

## Cache key generation

Cache keys for queries are derived from the LINQ expression tree:

1. `PartialEvaluator.Eval` folds captured local variables and other closed sub-expressions to constants, so different values of a captured variable produce different keys.
2. `Stringizing.ToString(expression)` walks the evaluated expression tree and produces a deterministic string.
3. The return type name (`typeof(T).Name`) is appended, separated by `\x1e`.
4. The combined string is hashed and prefixed with the Schemata domain marker via `ToCacheKey`.

Two queries that produce the same LINQ expression tree and target the same return type share a cache key.

## Commit-time eviction

Eviction runs after the database commit succeeds. This ordering closes the window where concurrent readers could repopulate the cache with pre-update data after an early eviction but before the database commit.

If the transaction rolls back, committed advisors do not run. The cache retains the pre-mutation entries until TTL expires.

## Options

`SchemataQueryCacheOptions` (configured via `UseQueryCache(o => ...)`):

| Property | Type | Default | Description |
|---|---|---|---|
| `Ttl` | `TimeSpan` | 5 minutes | Sliding expiration for cached results and reverse-index entries. |
| `EvictionEnabled` | `bool` | `true` | When `false`, committed eviction is skipped. Query and result advisors remain active; entries live until TTL expires. |

## Suppression

| Method | Marker | Effect |
|---|---|---|
| `repository.SuppressQueryCache()` | `QueryCacheSuppressed` | Skips `AdviceQueryCache` and `AdviceResultCache`. |
| `repository.SuppressQueryCacheEviction()` | `QueryCacheEvictionSuppressed` | Skips `AdviceCommittedEvictCache`. |

Use `Once()` to scope suppression to a single call:

```csharp
var fresh = await repository.Once()
    .SuppressQueryCache()
    .FirstOrDefaultAsync<Book>(q => q.Where(b => b.Uid == id), ct);
```

## Registration

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .UseQueryCache(o => o.Ttl = TimeSpan.FromMinutes(10));
```

`UseQueryCache` registers query, result, and committed eviction advisors as open-generic scoped services and registers `SchemataQueryCacheOptions`. A concrete `ICacheProvider` must be registered separately.

## Caveats

- Rollback skips eviction. If the database transaction rolls back, committed advisors do not run and stale cache entries remain until TTL expires.
- Aggregate queries and projections are cached but not reverse-indexed. They expire only via TTL, not via entity-level eviction.
- `DistributedCacheProvider` collection operations are single-process safe only. For multi-process deployments, use `RedisCacheProvider`.
- Cache and database are not atomic together. A crash between database commit and cache eviction leaves stale entries until TTL expires.

## See also

- [overview.md](overview.md) - `ICacheProvider` abstraction and provider selection
- [distributed.md](distributed.md) - `DistributedCacheProvider` (single-process safe collection ops)
- [redis.md](redis.md) - `RedisCacheProvider` (cluster-safe collection ops)
- [repository/caching.md](../repository/caching.md) - `UseQueryCache()` registration and options
- [repository/unit-of-work.md](../repository/unit-of-work.md) - explicit enlistment and committed advisors
- [core/advice-pipeline.md](../core/advice-pipeline.md) - `AdviseResult.Handle` semantics
