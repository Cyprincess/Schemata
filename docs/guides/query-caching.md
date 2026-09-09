# Query Caching

Add transparent query result caching to the `Student` repository with automatic eviction on update and delete. This
guide follows [Filtering and Pagination](filtering-and-pagination.md), but caching only requires the repository from
[Getting Started](getting-started.md), so you can skip the filter and mapping branches.

## How it works

Three advisors intercept the repository pipeline:

| Advisor                     | When                    | Behavior                                                       |
| --------------------------- | ----------------------- | -------------------------------------------------------------- |
| `AdviceQueryCache`          | Before query execution  | Returns cached result on hit, skips the database               |
| `AdviceResultCache`         | After successful query  | Stores result under the generation captured before execution  |
| `AdviceCommittedEvictCache` | After successful commit | Invalidates all cached queries for the changed entity type    |

Caching uses `ICacheProvider` - a pluggable abstraction with in-memory and Redis backends. The cache is opt-in: you must register a provider and call `UseQueryCache()`.

## Add the packages

Query caching ships outside the meta target packages, so add both packages explicitly:

```shell
dotnet add package --prerelease Schemata.Entity.Cache
dotnet add package --prerelease Schemata.Caching.Distributed
```

## Register the cache

In `Program.cs`, add the cache provider and append `UseQueryCache()` to the existing repository
registration. The following is the complete repository registration after [Unit of Work](unit-of-work.md):

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        // ...
        schema.ConfigureServices(services => {
            services.AddDistributedMemoryCache();
            services.AddDistributedCache();

            services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"))
                .WithUnitOfWork<AppDbContext>()
                .UseQueryCache();

            services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, AdviceAddStudentName>());
        });
        // ...
    });
```

`AddDistributedMemoryCache()` registers ASP.NET's in-memory `IDistributedCache`. `UseQueryCache()` registers query, result, and committed eviction advisors together with `SchemataQueryCacheOptions`.

If you skipped Unit of Work, omit `.WithUnitOfWork<AppDbContext>()`; query caching remains active, while
the open-transaction behavior below applies only when a repository joins a unit of work.

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
using (repository.SuppressQueryCache())
{
    var fresh = await repository.FirstOrDefaultAsync<Student>(q => q.Where(s => s.Uid == id), ct);
}
```

`SuppressQueryCache()` sets `QueryCacheSuppressed` in the `AdviceContext` and returns an `IDisposable`. The `using` scope restores the prior state on exit, so later operations on the same repository cache normally.

## Open write units of work

Enlisting a repository with `Join(uow)` automatically activates transaction-aware caching behavior. While the unit of
work has uncommitted writes, the query context carries `HasOpenWriteUnitOfWork`; `AdviceQueryCache` and
`AdviceResultCache` return `AdviseResult.Continue`, so reads inside the transaction hit the database and see
uncommitted changes instead of a stale cached copy. Caching resumes when the unit of work commits or rolls back.

## Commit-time eviction

Eviction runs after a successful database commit. Adding, updating, or removing an entity publishes a new generation for its type, invalidating entity results, counts, and projections. Late pre-commit readers can fill only their captured older generation. Results expire absolutely after the configured `Ttl`; hits do not extend their lifetime. Rollback skips invalidation. Database commit and cache publication are non-atomic, so a crash or cache failure between them can leave entries selectable until TTL expires. See [Query Cache](../documents/entity/query-cache.md) for the generation protocol.

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

Processes sharing the same Redis backing store observe the same entity-type generation. Process-local in-memory caches are independent and cannot propagate invalidation between instances. See [Redis](../documents/caching/redis.md) for provider key and deployment constraints.

## Verify

```shell
dotnet run
```

```shell
# First query hits the database (cache miss)
curl http://localhost:5000/v1/students

# Second identical query returns from cache
curl http://localhost:5000/v1/students

# Update a student - evicts cached queries containing that student
curl -X PATCH "http://localhost:5000/v1/students/<name>" \
     -H "Content-Type: application/json" \
     -d '{"age":22}'

# Next list query hits the database again and re-caches
curl http://localhost:5000/v1/students
```

## Next steps

- [Validation](validation.md) — add input validation with FluentValidation
- [Unit of Work](unit-of-work.md) — committed advisors batch their evictions per commit
- [Concurrency and Freshness](concurrency-and-freshness.md) — ETags pair naturally with cached reads

## See also

- [Query Cache](../documents/entity/query-cache.md) — cache advisors, generations, eviction design
- [Distributed Cache](../documents/caching/distributed.md) — `ICacheProvider`, `IndexLocks`
