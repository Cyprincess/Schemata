# Distributed Cache

## What you'll build

A Schemata application that caches repository query results using the distributed cache abstraction, then swaps the in-memory backing store for Redis. You'll configure TTL, understand the eviction strategy, and learn the safety boundaries of each adapter.

## Prerequisites

- The Student example from [Getting Started](../guides/getting-started.md) is running.
- NuGet packages: `Schemata.Entity.Cache`, `Schemata.Caching.Distributed` (in-memory path) or `Schemata.Caching.Redis` (Redis path).
- For Redis: a running Redis instance and `StackExchange.Redis`.

## Step 1: Enable query caching with the in-memory distributed cache

The `IDistributedCache` abstraction from `Microsoft.Extensions.Caching.Distributed` is the backing store. Start with the built-in in-memory implementation for local development.

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseJsonSerializer();

        schema.ConfigureServices(services => {
            services.AddDistributedMemoryCache();   // IDistributedCache backed by memory
            services.AddDistributedCache();         // wraps IDistributedCache as ICacheProvider

            services.AddRepository(typeof(EfCoreRepository<,>))
                    .UseEntityFrameworkCore<AppDbContext>(
                        (_, opts) => opts.UseSqlite("Data Source=app.db"))
                    .UseQueryCache(o => o.Ttl = TimeSpan.FromMinutes(5));
        });

        schema.UseResource()
              .MapHttp()
              .Use<Student>();
    });
```

`AddDistributedCache()` registers `DistributedCacheProvider` as the `ICacheProvider` singleton with `TryAddSingleton`, so it does not replace an existing registration. `UseQueryCache` lives on `SchemataRepositoryBuilder` — chain it after `AddRepository`.

**Assertion:** `GET /students` returns `200 OK`. A second identical request within 5 minutes returns the same result without hitting the database (confirm by adding a query log to the EF context).

## Step 2: Configure TTL and understand the cache key

`SchemataQueryCacheOptions.Ttl` is the sliding expiration applied to cached results and reverse-index entries (default 5 minutes). The cache key comes from `QueryContext.ToCacheKey()`, which stringizes the built LINQ expression tree — so the filter, ordering, and `Skip`/`Take` operators all factor into the key — appends `typeof(T).FullName`, and hashes the result. Two queries with different LINQ produce different keys.

```csharp
services.AddRepository(typeof(EfCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>(
            (_, opts) => opts.UseSqlite("Data Source=app.db"))
        .UseQueryCache(o => o.Ttl = TimeSpan.FromSeconds(30)); // short TTL for hot data
```

**Assertion:** After the TTL expires, the next request hits the database again. Verify by watching EF Core query logs.

## Step 3: Swap to Redis

Replace `AddDistributedMemoryCache` and `AddDistributedCache` with the Redis equivalents. The `ICacheProvider` contract is identical; no other code changes are needed.

```csharp
services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
services.AddRedisCache();   // registers RedisCacheProvider as ICacheProvider
```

`RedisCacheProvider` resolves `IConnectionMultiplexer` and calls `GetDatabase()`. `AddRedisCache()` uses `TryAddSingleton<ICacheProvider, RedisCacheProvider>()`; if `AddDistributedCache()` ran first, `AddRedisCache()` is silently ignored. Register exactly one provider.

**Assertion:** With Redis running, `GET /students` populates a key in Redis (verify with `redis-cli KEYS "*"`). Restart the app and the first request still returns cached data from Redis.

## Step 4: Understand the eviction strategy

`UseQueryCache` registers three advisors:

| Advisor | Interface | What it does |
| --- | --- | --- |
| `AdviceQueryCache` | `IRepositoryQueryAdvisor` | Returns cached result on hit; `AdviseResult.Handle` short-circuits DB |
| `AdviceResultCache` | `IRepositoryResultAdvisor` | Writes query result to cache after DB execution |
| `AdviceCommittedEvictCache` | `IRepositoryCommittedAdvisor` | Evicts cache entries for updated and removed entities after commit |

Eviction happens in the committed advisor pipeline. If the transaction rolls back, committed advisors do not run and the cache retains the pre-update value. A rolled-back write should not invalidate a valid cached read.

**Assertion:** Update a student and immediately query. The response reflects the updated data because the eviction ran after the successful commit.

## Step 5: Suppress caching for a specific query

When you need a fresh read that bypasses the cache, scope `SuppressQueryCache()` on the repository instance:

```csharp
using (repository.SuppressQueryCache())
{
    var fresh = await repository.FirstOrDefaultAsync<Student>(
        q => q.Where(s => s.Name == "alice"), ct);
}
```

`SuppressQueryCache()` sets `QueryCacheSuppressed` in the `AdviceContext` and returns an `IDisposable` that restores the prior state. While the marker is present, `AdviceQueryCache` and `AdviceResultCache` return `AdviseResult.Continue`, so the query reaches the database and its result is not cached.

**Assertion:** A suppressed query always hits the database even when a cached result exists for the same key.

## Common pitfalls

- **`UseQueryCache` lives on `SchemataRepositoryBuilder`.** Chain it after `AddRepository`. The `ICacheProvider` (via `AddDistributedCache` or `AddRedisCache`) must also be registered, separately.
- **`AddDistributedCache` and `AddRedisCache` both use `TryAddSingleton`.** Calling both registers only the first. Pick one per application.
- **`DistributedCacheProvider` is single-process safe only for collection operations.** The reverse index that maps an entity to its cache keys is stored in the same `IDistributedCache`. With an in-memory backend, each process holds its own index, so eviction in one process does not reach the others. Use Redis for multi-process deployments.
- **Redis collection operations are cluster-safe.** `RedisCacheProvider` uses native Redis Set commands (`SADD`, `SMEMBERS`, `SREM`, `DEL`) for the reverse index, atomic at the server. (Its atomic compare-and-swap key-value operations use Lua scripts; the query cache does not use them.)
- **Rollback skips eviction.** If `CommitAsync` throws and the transaction rolls back, committed advisors do not run. The cache may serve stale data until the TTL expires.

## See also

- [Query caching guide](../guides/query-caching.md)
- [Caching overview](../documents/caching/overview.md)
- [Distributed cache](../documents/caching/distributed.md)
- [Redis cache](../documents/caching/redis.md)
- [Query cache](../documents/entity/query-cache.md)
