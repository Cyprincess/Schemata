# Repository Caching

The `Schemata.Entity.Cache` package adds distributed query caching and automatic eviction to the repository layer. It is opt-in: call `UseQueryCache()` on the repository builder to activate it.

For the full caching subsystem reference, see [caching/query-cache.md](../caching/query-cache.md).

## Where the code lives

| Item | Path |
|---|---|
| `UseQueryCache` extension | `src/Schemata.Entity.Cache/Extensions/SchemataRepositoryBuilderExtensions.cs` |
| Cache advisors | `src/Schemata.Entity.Cache/Advisors/` |
| `SchemataQueryCacheOptions` | `src/Schemata.Entity.Cache/SchemataQueryCacheOptions.cs` |
| `ICacheProvider` | `src/Schemata.Caching.Skeleton/ICacheProvider.cs` |

## Enabling query caching

```csharp
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .UseQueryCache(o => o.Ttl = TimeSpan.FromMinutes(10));
```

`UseQueryCache` registers three open-generic scoped advisors:

| Advisor | Interface | Order |
|---|---|---|
| `AdviceQueryCache<,,>` | `IRepositoryQueryAdvisor<,,>` | 100,000,000 |
| `AdviceResultCache<,,>` | `IRepositoryResultAdvisor<,,>` | 100,000,000 |
| `AdviceCommittedEvictCache<>` | `IRepositoryCommittedAdvisor<>` | 900,000,000 |

A concrete `ICacheProvider` must be registered separately. Use `DistributedCacheProvider` for single-process deployments or `RedisCacheProvider` for multi-process / cluster deployments.

## Options

`SchemataQueryCacheOptions` (configured via `UseQueryCache(o => ...)`):

| Property | Default | Description |
|---|---|---|
| `Ttl` | 5 minutes | Sliding expiration for cached results and reverse-index entries. |
| `EvictionEnabled` | `true` | When `false`, committed eviction is skipped; entries live until TTL expires. |

## Suppression

| Method | Marker | Effect |
|---|---|---|
| `repository.SuppressQueryCache()` | `QueryCacheSuppressed` | Skips `AdviceQueryCache` and `AdviceResultCache` for this instance. |
| `repository.SuppressQueryCacheEviction()` | `QueryCacheEvictionSuppressed` | Skips `AdviceCommittedEvictCache` for this instance. |

Use `Once()` to scope suppression to a single call:

```csharp
var fresh = await repository.Once()
    .SuppressQueryCache()
    .FirstOrDefaultAsync<Book>(q => q.Where(b => b.Uid == id));
```

## Commit-time eviction

`AdviceCommittedEvictCache` receives `CommitChanges<TEntity>` after a successful standalone repository commit or unit-of-work commit. It evicts reverse-indexed entries for updated and removed entities. If the transaction rolls back, committed advisors do not run and the cache retains the pre-mutation entries until TTL expires.

## See also

- [caching/query-cache.md](../caching/query-cache.md) - full advisor reference, reverse index, and cache key generation
- [caching/overview.md](../caching/overview.md) - `ICacheProvider` abstraction and provider selection
- [caching/distributed.md](../caching/distributed.md) - `DistributedCacheProvider` (single-process safe)
- [caching/redis.md](../caching/redis.md) - `RedisCacheProvider` (cluster-safe)
- [unit-of-work.md](unit-of-work.md) - explicit enlistment and committed advisors
