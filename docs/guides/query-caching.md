# Query Caching

Add transparent query result caching to the `Student` repository with automatic eviction on update and delete. This guide builds on [Getting Started](getting-started.md).

## How it works

Three advisors intercept the repository pipeline:

| Advisor | When | Behavior |
| ------- | ---- | -------- |
| `AdviceQueryCache` | Before query execution | Returns cached result on hit, skips the database |
| `AdviceResultCache` | After successful query | Stores result in cache and updates the reverse index |
| `AdviceCommittedEvictCache` | After successful commit | Evicts cached queries that contain updated or removed entities |

Caching uses `ICacheProvider` - a pluggable abstraction with in-memory and Redis backends. The cache is opt-in: you must register a provider and call `UseQueryCache()`.

## Add the packages

`Schemata.Application.Complex.Targets` already includes `Schemata.Entity.Cache`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Entity.Cache
dotnet add package --prerelease Schemata.Caching.Distributed
```

## Register the cache

In `Program.cs`, add the cache provider and enable query caching on the repository builder:

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        // ...
        schema.ConfigureServices(services => {
            services.AddDistributedMemoryCache();
            services.AddDistributedCache();

            services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"))
                .UseQueryCache();

            services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
        });
        // ...
    });
```

`AddDistributedMemoryCache()` registers ASP.NET's in-memory `IDistributedCache`. `UseQueryCache()` registers query, result, and committed eviction advisors together with `SchemataQueryCacheOptions`.

## Configure TTL and eviction

Pass an optional delegate to `UseQueryCache` to customize behavior:

```csharp
.UseQueryCache(options => {
    options.Ttl             = TimeSpan.FromMinutes(10); // default: 5 minutes
    options.EvictionEnabled = false;                    // rely on TTL only
})
```

`EvictionEnabled = false` disables `AdviceCommittedEvictCache`. The query and result advisors remain active; entries live until TTL expires.

## Suppress caching for a single query

```csharp
var fresh = await repository.Once()
    .SuppressQueryCache()
    .FirstOrDefaultAsync(q => q.Where(s => s.Uid == id), ct);
```

`SuppressQueryCache()` sets `QueryCacheSuppressed` in the `AdviceContext`. `Once()` creates a fresh repository instance so the suppression doesn't affect later operations on the caller's repository.

## Commit-time eviction

Eviction runs through `IRepositoryCommittedAdvisor<TEntity>` after `CommitAsync` succeeds. The advisor receives a `CommitChanges<TEntity>` snapshot and evicts reverse-indexed entries for updated and removed entities. If a unit of work rolls back, committed advisors do not run and cached entries remain valid until TTL expires.

## Production: Redis

For multi-process deployments, replace the in-memory provider with Redis:

```shell
dotnet add package --prerelease Schemata.Caching.Redis
```

```csharp
services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
services.AddRedisCache();
```

`RedisCacheProvider` uses native Redis Set commands for the reverse index, making eviction cluster-safe. The in-memory `IDistributedCache` adapter is single-process safe only on collection operations.

## Verify

```shell
dotnet run
```

```shell
# First query hits the database (cache miss)
curl http://localhost:5000/students

# Second identical query returns from cache
curl http://localhost:5000/students

# Update a student - evicts cached queries containing that student
curl -X PATCH "http://localhost:5000/students/<name>" \
     -H "Content-Type: application/json" \
     -d '{"age":22}'

# Next list query hits the database again and re-caches
curl http://localhost:5000/students
```

## See also

- [Filtering and Pagination](filtering-and-pagination.md) - previous in the series: AIP-160 filter and AIP-132 order
- [Validation](validation.md) - next in the series: input validation with FluentValidation
- [Unit of Work](unit-of-work.md) - explicit enlistment and committed advisors
- [Query Cache](../documents/caching/query-cache.md) - cache advisors, reverse index, eviction design
- [Distributed Cache](../documents/caching/distributed.md) - `ICacheProvider`, `IndexLocks`
