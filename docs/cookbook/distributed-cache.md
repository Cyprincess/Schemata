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

        schema.Services.AddDistributedMemoryCache();   // IDistributedCache backed by memory
        schema.Services.AddDistributedCache();         // wraps IDistributedCache as ICacheProvider

        schema.UseResource()
              .MapHttp()
              .UseRepository<EntityFrameworkCoreRepository<AppDbContext>>()
              .UseQueryCache(o => {
                  o.Ttl = TimeSpan.FromMinutes(5);
              })
              .Use<Student>();
    });
```

`AddDistributedCache()` registers `DistributedCacheProvider` as the `ICacheProvider` singleton using `TryAddSingleton`, so it won't replace an existing registration. `UseQueryCache` lives on `SchemataRepositoryBuilder` and must be chained after `UseRepository`.

**Assertion:** `GET /students` returns `200 OK`. A second identical request within 5 minutes returns the same result without hitting the database (confirm by adding a query log to the EF context).

## Step 2: Configure TTL and understand the cache key

`SchemataQueryCacheOptions.Ttl` controls how long a cached result lives. The cache key is derived from the query context via `QueryContext.ToCacheKey()`, which encodes the entity type, predicate expression, ordering, and pagination parameters. Two queries with different filters produce different keys.

```csharp
schema.UseRepository<EntityFrameworkCoreRepository<AppDbContext>>()
      .UseQueryCache(o => {
          o.Ttl = TimeSpan.FromSeconds(30);   // short TTL for frequently-changing data
      });
```

**Assertion:** After the TTL expires, the next request hits the database again. Verify by watching EF Core query logs.

## Step 3: Swap to Redis

Replace `AddDistributedMemoryCache` and `AddDistributedCache` with the Redis equivalents. The `ICacheProvider` contract is identical; no other code changes are needed.

```csharp
schema.Services.AddStackExchangeRedisCache(o => {
    o.Configuration = "localhost:6379";
});
schema.Services.AddRedisCache();   // registers RedisCacheProvider as ICacheProvider
```

`AddRedisCache()` uses `TryAddSingleton<ICacheProvider, RedisCacheProvider>()`. If `AddDistributedCache()` was called first, `AddRedisCache()` is silently ignored. Call only one.

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

When you need a fresh read that bypasses the cache, call `SuppressQueryCache()` on the repository instance:

```csharp
var fresh = await repository.SuppressQueryCache()
                            .FirstOrDefaultAsync<Student>(s => s.Name == "alice");
```

`SuppressQueryCache()` sets `QueryCacheSuppressed` in the `AdviceContext`. `AdviceQueryCache` checks for this marker and returns `AdviseResult.Continue`, letting the query reach the database.

**Assertion:** A suppressed query always hits the database even when a cached result exists for the same key.

## Common pitfalls

- **`UseQueryCache` lives on `SchemataRepositoryBuilder`.** Chain it after `UseRepository`: `UseRepository(...).UseQueryCache(...)`. Calling it before `UseRepository` fails to compile because the builder does not yet exist.
- **`AddDistributedCache` and `AddRedisCache` both use `TryAddSingleton`.** Calling both registers only the first one. Pick one per application.
- **`DistributedCacheProvider` is single-process safe only for collection operations.** The distributed adapter uses a reverse index to track which cache keys belong to an entity. That index is stored in the same `IDistributedCache` instance. In a multi-process deployment with an in-memory cache, each process has its own index and eviction in one process does not affect the other. Use Redis for multi-process deployments.
- **Redis is cluster-safe.** `RedisCacheProvider` uses atomic Lua scripts for index updates, making it safe across Redis cluster nodes.
- **Rollback skips eviction.** If `CommitAsync` throws and the transaction rolls back, committed advisors do not run. The cache may serve stale data until the TTL expires. Design TTL values with this in mind for high-consistency scenarios.

## See also

- [Query caching guide](../guides/query-caching.md)
- [Caching overview](../documents/caching/overview.md)
- [Distributed cache](../documents/caching/distributed.md)
- [Redis cache](../documents/caching/redis.md)
- [Query cache](../documents/caching/query-cache.md)
