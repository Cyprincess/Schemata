# Repository Caching

The `Schemata.Entity.Cache` package adds distributed query caching and automatic eviction to the repository layer. It is opt-in: call `UseQueryCache()` on the repository builder to activate it.

For the full query-cache reference, see [entity/query-cache.md](../entity/query-cache.md).

## Where the code lives

| Item                        | Path                                                                          |
| --------------------------- | ----------------------------------------------------------------------------- |
| `UseQueryCache` extension   | `src/Schemata.Entity.Cache/Extensions/SchemataRepositoryBuilderExtensions.cs` |
| Cache advisors              | `src/Schemata.Entity.Cache/Advisors/`                                         |
| `SchemataQueryCacheOptions` | `src/Schemata.Entity.Cache/SchemataQueryCacheOptions.cs`                      |
| `ICacheProvider`            | `src/Schemata.Caching.Skeleton/ICacheProvider.cs`                             |

## Enabling query caching

```csharp
services.AddRepository<Book, EfCoreRepository<AppDbContext, Book>>()
        .UseQueryCache(o => o.Ttl = TimeSpan.FromMinutes(10));
```

`UseQueryCache` registers three open-generic scoped advisors:

| Advisor                       | Interface                       | Order       |
| ----------------------------- | ------------------------------- | ----------- |
| `AdviceQueryCache<,,>`        | `IRepositoryQueryAdvisor<,,>`   | 100,000,000 |
| `AdviceResultCache<,,>`       | `IRepositoryResultAdvisor<,,>`  | 100,000,000 |
| `AdviceCommittedEvictCache<>` | `IRepositoryCommittedAdvisor<>` | 900,000,000 |

A concrete `ICacheProvider` must be registered separately. Use `DistributedCacheProvider` for single-process deployments or `RedisCacheProvider` for multi-process / cluster deployments.

## Options

`SchemataQueryCacheOptions` (configured via `UseQueryCache(o => ...)`):

| Property          | Default   | Description                                                                  |
| ----------------- | --------- | ---------------------------------------------------------------------------- |
| `Ttl`             | 5 minutes | Sliding expiration for cached results and reverse-index entries.             |
| `EvictionEnabled` | `true`    | When `false`, committed eviction is skipped; entries live until TTL expires. |

## Suppression

| Method                                    | Marker                         | Effect                                                              |
| ----------------------------------------- | ------------------------------ | ------------------------------------------------------------------- |
| `repository.SuppressQueryCache()`         | `QueryCacheSuppressed`         | Skips `AdviceQueryCache` and `AdviceResultCache` for this instance. |
| `repository.SuppressQueryCacheEviction()` | `QueryCacheEvictionSuppressed` | Skips `AdviceCommittedEvictCache` for this instance.                |

Scope a suppression with `using`:

```csharp
using (repository.SuppressQueryCache())
{
    var fresh = await repository.FirstOrDefaultAsync<Book>(q => q.Where(b => b.Uid == id), ct);
}
```

## Commit-time eviction

`AdviceCommittedEvictCache` receives `CommitChanges<TEntity>` after a successful standalone repository commit or unit-of-work commit. It evicts reverse-indexed entries for updated and removed entities. If the transaction rolls back, committed advisors do not run and the cache retains the pre-mutation entries until TTL expires.

## Open write units of work

While a repository has an open write unit of work — enlisted in a shared `IUnitOfWork`, or holding an
implicit one with pending adds, updates, or removes, and not yet completed — both cache advisors stand
down: `AdviceQueryCache` skips cache reads and `AdviceResultCache` skips cache writes. The flag
(`QueryContext.HasOpenWriteUnitOfWork`, surfaced from the repository) keeps uncommitted state out of
the cache, so a rollback can never leave phantom entries behind. After the commit completes, the next
query repopulates the cache as usual.

## See also

- [entity/query-cache.md](../entity/query-cache.md) — full advisor reference, reverse index, and cache key generation
- [caching/overview.md](../caching/overview.md) — `ICacheProvider` abstraction and provider selection
- [unit-of-work.md](unit-of-work.md) — the committed pipeline that drives eviction
