# Query Cache

The `Schemata.Entity.Cache` package adds transparent query caching and automatic invalidation to the repository layer. Results are serialized to JSON and stored in `ICacheProvider`. Cache keys combine the LINQ expression with an entity-type generation. Invalidation publishes a new generation after a successful repository commit.

## Where the code lives

| Item                          | Path                                                                          |
| ----------------------------- | ----------------------------------------------------------------------------- |
| `AdviceQueryCache<,,>`        | `src/Schemata.Entity.Cache/Advisors/AdviceQueryCache.cs`                      |
| `AdviceResultCache<,,>`       | `src/Schemata.Entity.Cache/Advisors/AdviceResultCache.cs`                     |
| `AdviceCommittedEvictCache<>` | `src/Schemata.Entity.Cache/Advisors/AdviceCommittedEvictCache.cs`             |
| `CacheGeneration<TEntity>`    | `src/Schemata.Entity.Cache/CacheGeneration.cs`                                |
| `Stringizing`                 | `src/Schemata.Entity.Cache/Stringizing.cs`                                    |
| `Evaluator.PartialEval`       | `src/Schemata.Common/Evaluator.cs` (shared)                                   |
| `SchemataQueryCacheOptions`   | `src/Schemata.Entity.Cache/SchemataQueryCacheOptions.cs`                      |
| `UseQueryCache` extension     | `src/Schemata.Entity.Cache/Extensions/SchemataRepositoryBuilderExtensions.cs` |

## The cache advisors

### AdviceQueryCache

**Interface:** `IRepositoryQueryAdvisor<TEntity, TResult, T>`
**Order:** 100,000,000 (`SchemataConstants.Orders.Base`)

Runs before the query executes against the database. On a cache hit, sets `context.Result` and returns `AdviseResult.Handle`, short-circuiting database execution entirely.

**Steps:**

1. If `QueryCacheSuppressed` is in the advice context, or `context.HasOpenWriteUnitOfWork` is set
   (the repository is enlisted or holds an implicit write unit of work with pending changes), returns
   `Continue` — uncommitted state never satisfies a cache read.
2. Calls `context.ToCacheKey()` to derive the cache key from the query expression. If the key is null or whitespace, returns `Continue`.
3. Reads the entity-type generation from `ICacheProvider`. If absent, attempts to add a fresh random token and then reads the stored token. If it is still absent, returns `Continue`.
4. Captures the versioned result key in cache-owned state keyed by this `QueryContext`. The snapshot is immutable and independent of other queries sharing the same advice context.
5. Reads the versioned result key. On a miss or a JSON `null` result, returns `Continue`; otherwise sets `context.Result` and returns `Handle`.

### AdviceResultCache

**Interface:** `IRepositoryResultAdvisor<TEntity, TResult, T>`
**Order:** 100,000,000 (`SchemataConstants.Orders.Base`)

Runs after the query executes and `context.Result` is populated. Stores the result under the generation captured before database execution.

**Steps:**

1. If `QueryCacheSuppressed` is in the advice context, or `context.HasOpenWriteUnitOfWork` is set,
   returns `Continue` — uncommitted results are never written to the cache.
2. If `context.Result` is null, returns `Continue`.
3. Uses the snapshot captured by the query advisor. If this context has no snapshot, skips the fill; reading the current generation here could publish old data into a post-commit generation.
4. Serializes `context.Result` via `JsonSerializer.SerializeToUtf8Bytes` and calls `ICacheProvider.SetAsync` with `AbsoluteExpirationRelativeToNow = SchemataQueryCacheOptions.Ttl` (default 5 minutes).
5. Returns `Continue`.

Single entities, aggregate queries (`AnyAsync`, `CountAsync`, `LongCountAsync`), and projections all share their root entity type's generation.

### AdviceCommittedEvictCache

**Interface:** `IRepositoryCommittedAdvisor<TEntity>`
**Order:** 900,000,000 (`SchemataConstants.Orders.Max`)

Runs after a standalone repository commit or unit-of-work commit succeeds. It receives a `CommitChanges<TEntity>` snapshot and invalidates all cached queries rooted in `TEntity` when entities were added, updated, or removed.

**Steps:**

1. If `SchemataQueryCacheOptions.EvictionEnabled` is `false`, or if `QueryCacheEvictionSuppressed` is in the advice context, returns `Continue`.
2. If all three change collections are empty, returns `Continue`.
3. Publishes a fresh random generation token with `ICacheProvider.SetAsync`, with no expiration.
4. Returns `Continue`.

## Entity-type generations

`CacheGeneration<TEntity>` stores one non-expiring metadata entry per entity type in the selected provider's key space. The metadata key hashes the `generation` discriminator and the assembly-qualified entity type name. Result keys include this metadata key, the captured token, and the expression-derived key.

The generation is captured before database execution. A pre-commit query may finish late and store its result under its old generation, but queries started after successful invalidation read the newly published generation. Added entities also invalidate counts and projections, even when no entity previously appeared in their results. This is deliberately coarser than per-entity eviction: any committed change invalidates every cached query for that root type.

Each initialization or invalidation publishes a unique token only once. Concurrent invalidations can abandon freshly cached results and cause additional misses, but cannot restore an old generation. Initialization uses `TryAddAsync` followed by a read of the stored token. Even when a provider only serializes `TryAddAsync` within one process, a delayed initializer can only publish an unused token; database execution begins after initialization completes.

Generation metadata stays unexpired. Losing or removing it causes a fresh random token to be created, never a reusable default. Result entries have a bounded absolute lifetime, so abandoned generations age out even under repeated access.

## Cache key generation

Cache keys combine provider command identity with the query's structural identity:

1. The repository implements `IQueryCacheKeyProvider`. EF Core translates the query with
   `CreateDbCommand`; LinqToDB uses `ToSqlQuery` with parameter inlining disabled. The key includes
   the provider, data-source identity, SQL, and ordered parameter names, database types, and values.
2. `QueryCacheKey.Create` encodes supported scalar and scalar-array values with type and length
   boundaries, then hashes the material with SHA-256. Connection strings and parameter values
   stay out of the returned key. Unsupported parameter values disable caching for that query.
3. `Stringizing.ToStructure` adds the LINQ structure, including client projection member/method
   identities and captured field values. Partial evaluation preserves calls, property reads,
   constructors, user operators, and byref-like expressions. One provider root is represented by
   an opaque marker alongside the provider key; additional extension nodes and unsupported shapes
   disable caching rather than using a debug-string fallback.
4. `QueryContext.Operation` distinguishes `FirstOrDefault`, `SingleOrDefault`, `Any`, `Count`, and
   `LongCount`. The result type is included, so a cached first result cannot bypass a later
   single-result cardinality check.
5. The combined identity is hashed through `ToCacheKey` and paired with the root entity generation.

Repositories without `IQueryCacheKeyProvider`, non-relational EF providers, and ephemeral SQLite
connections bypass both cache reads and fills. File-backed relational queries retain caching.
Applications whose connection session or interceptors change query meaning independently of the
captured identity must suppress caching for those operations. Cross-entity invalidation retains
the root-entity limitation described below.

## Commit-time eviction

Eviction runs after the database commit succeeds. Once the generation write completes, later queries cannot select entries from an earlier generation, including entries filled late by pre-commit readers. A query already in flight may still return the data it read before that boundary.

If the transaction rolls back, committed advisors do not run. The cache retains the pre-mutation entries until TTL expires.

## Options

`SchemataQueryCacheOptions` (configured via `UseQueryCache(o => ...)`):

| Property          | Type       | Default   | Description                                                                                                           |
| ----------------- | ---------- | --------- | --------------------------------------------------------------------------------------------------------------------- |
| `Ttl`             | `TimeSpan` | 5 minutes | Absolute lifetime for cached results; generation metadata does not expire.                                            |
| `EvictionEnabled` | `bool`     | `true`    | When `false`, committed eviction is skipped. Query and result advisors remain active; entries live until TTL expires. |

## Suppression

| Method                                    | Marker                         | Effect                                            |
| ----------------------------------------- | ------------------------------ | ------------------------------------------------- |
| `repository.SuppressQueryCache()`         | `QueryCacheSuppressed`         | Skips `AdviceQueryCache` and `AdviceResultCache`. |
| `repository.SuppressQueryCacheEviction()` | `QueryCacheEvictionSuppressed` | Skips `AdviceCommittedEvictCache`.                |

Scope a suppression with `using`:

```csharp
using (repository.SuppressQueryCache())
{
    var fresh = await repository.FirstOrDefaultAsync<Book>(q => q.Where(b => b.Uid == id), ct);
}
```

## Registration

```csharp
services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
        .UseQueryCache(o => o.Ttl = TimeSpan.FromMinutes(10));
```

`UseQueryCache` registers query, result, and committed eviction advisors as open-generic scoped services and registers `SchemataQueryCacheOptions`. A concrete `ICacheProvider` must be registered separately.

## Caveats

- Rollback skips invalidation because the database changes were not committed.
- Invalidation covers the query's root entity type. Queries depending on changes to other entity types need application-managed cache suppression or another invalidation policy.
- Processes must share the same provider backing store and consistent key reads/writes for cross-process invalidation. Process-local caches remain independent.
- Cache and database commits are not atomic together. A crash or cache failure between database commit and generation publication can leave old entries selectable until their absolute TTL expires. Generation publication failures propagate to the caller.

## See also

- [caching/overview.md](../caching/overview.md) — `ICacheProvider` abstraction and provider selection
- [repository/caching.md](../repository/caching.md) — `UseQueryCache()` registration and options
- [repository/unit-of-work.md](../repository/unit-of-work.md) — the committed pipeline that drives eviction
