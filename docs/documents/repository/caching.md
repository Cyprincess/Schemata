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

A concrete `ICacheProvider` backend must be registered separately. Use `MemoryCacheProvider` for single-process deployments or `RedisCacheProvider` for multi-process / cluster deployments.

## Options

`SchemataQueryCacheOptions` (configured via `UseQueryCache(o => ...)`):

| Property | Default   | Description                                                                |
| -------- | --------- | -------------------------------------------------------------------------- |
| `Ttl`    | 5 minutes | Absolute lifetime of cached results; generation metadata does not expire.  |

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

`AdviceCommittedEvictCache` runs after a successful standalone repository commit or unit-of-work commit, once per committing repository enlistment that staged at least one write. The notification is type-level, so any committed write publishes a fresh type-wide generation, invalidating entity results, aggregates, and projections alike; a commit with no writes sends no notification. Queries capture their generation before database execution, so late pre-commit fills cannot repopulate the current generation. If the transaction rolls back, committed advisors do not run. Database commit and cache invalidation remain non-atomic; a failure between them can leave entries selectable until their absolute TTL expires.

## Open write units of work

Every active enlistment bypasses shared cache reads and fills from the moment of `Begin()` or
`Join()`, including a repository with no local writes. Implicit units of work created by a
standalone mutation use the same gate, `QueryContext.HasOpenWriteUnitOfWork`. Rollback and
uncommitted disposal preserve the shared generation. Commit publishes type-level invalidation
before resource/domain callbacks execute.

External enlistments remain one-shot after completion; resolve a fresh repository to read and
repopulate the cache. After a successful implicit `repository.CommitAsync()`, that repository
reopens for standalone queries and may cache their results.

Cache bypass changes only shared-cache access. EF Core's buffered staging remains invisible to
scalar SQL queries until save, while LinqToDB's immediate SQL writes are visible through the joined
transaction. Repository queries never flush EF staging. See the provider read-result boundary in
[entity/query-cache.md](../entity/query-cache.md#commit-time-eviction).

## See also

- [entity/query-cache.md](../entity/query-cache.md) — full advisor reference, generations, and cache key generation
- [caching/overview.md](../caching/overview.md) — `ICacheProvider` contract and provider selection
- [unit-of-work.md](unit-of-work.md) — the committed pipeline that drives eviction
