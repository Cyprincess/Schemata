# Query Caching

This guide adds distributed query caching to the Student CRUD app. Building on [Filtering and Pagination](filtering-and-pagination.md), you will enable transparent caching of repository queries with automatic eviction on update and delete.

## How it works

Schemata's entity query cache intercepts repository queries through the advisor pipeline. Before a query hits the database, the cache advisor checks for a cached result. After a successful query, the result is stored in the cache. On update or delete, all cached query results containing that entity are automatically evicted via a reverse index.

Caching uses `ICacheProvider` — a pluggable abstraction with in-memory and Redis backends. The cache is **opt-in**: you must register a provider and call `UseQueryCache()`.

## Add the cache packages

```shell
dotnet add package --prerelease Schemata.Entity.Cache
dotnet add package --prerelease Schemata.Caching.Distributed
```

`schemata.Entity.Cache` provides the query cache advisors and eviction logic. `Schemata.Caching.Distributed` provides the in-memory `ICacheProvider` implementation.

## Register the cache

In `Program.cs`, add the cache provider and enable query caching on the repository builder:

```csharp
// Register a cache provider (in-memory for development)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddDistributedCache();

schema.ConfigureServices(services => {
    services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>(
            (_, opts) => opts.UseSqlite("Data Source=app.db"))
        .WithUnitOfWork<AppDbContext>()
        .UseQueryCache();

    // ... existing advisor registrations ...
});
```

`AddDistributedMemoryCache()` registers ASP.NET's in-memory `IDistributedCache`. `AddDistributedCache()` wraps it as `ICacheProvider`. `UseQueryCache()` registers four advisors:

| Advisor                     | When                            | Behavior                                    |
| --------------------------- | ------------------------------- | ------------------------------------------- |
| `AdviceQueryCache`          | Before query execution          | Returns cached result on hit (skips DB)     |
| `AdviceResultCache`         | After successful query          | Stores result in cache + reverse index      |
| `AdviceUpdateEvictCache`    | During entity update            | Evicts all cached queries containing the entity |
| `AdviceRemoveEvictCache`    | During entity remove            | Same eviction for deletes                   |

## Verify

```shell
dotnet run
```

### Observe cache behavior

```shell
# First query hits the database (cache miss)
curl http://localhost:5000/students

# Second identical query returns from cache (no DB round-trip)
curl http://localhost:5000/students

# Update a student — evicts cached queries containing that student
curl -X PATCH http://localhost:5000/students/1 \
     -H "Content-Type: application/json" \
     -d '{"age":22}'

# Next list query hits the database again (cache was evicted) and re-caches
curl http://localhost:5000/students
```

The cache behavior is transparent to clients — responses are identical regardless of whether they came from the cache or the database. ETags and timestamps are included in cached responses exactly as they were when the result was stored.

## Cache configuration

Customize cache TTL and eviction behavior via `UseQueryCache` options:

```csharp
.UseQueryCache(options => {
    options.Ttl = TimeSpan.FromMinutes(10);  // default: 5 minutes
    options.EvictionEnabled = false;         // rely on TTL expiration only
})
```

## Bypassing the cache

To force a database query for a specific operation:

```csharp
// On IRepository<TEntity>
var fresh = await repository.Once()
    .SuppressQueryCache()
    .FirstOrDefaultAsync(q => q.Where(e => e.Id == id), ct);
```

`SuppressQueryCache()` sets a flag in the `AdviceContext` that both `AdviceQueryCache` and `AdviceResultCache` respect. `Once()` creates a temporary repository instance so the suppression doesn't affect other operations.

## What gets cached

- Scalar queries: `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, `LongCountAsync`
- The cache key is derived from the LINQ expression tree (via `Stringizing`) and the return type name, hashed with CityHash128
- Two queries that produce the same expression and return type share a cache key
- Only results of type `TEntity` are reverse-indexed for precise eviction; projections and aggregates are cached but evicted only at TTL expiry

## Production: Redis

For multi-process deployments, replace the in-memory provider with Redis:

```shell
dotnet add package --prerelease Schemata.Caching.Redis
```

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddRedisCache();
```

`RedisCacheProvider` uses native Redis Set commands for the reverse index, making eviction cluster-safe without in-process locking.

## Next steps

- [Validation](validation.md) — add input validation with FluentValidation
- [Query Cache Reference](../documents/repository/caching.md) — technical details
- [Cache Providers](../documents/cache/overview.md) — `ICacheProvider` architecture

## Further reading

- [Query Pipeline](../documents/repository/query-pipeline.md)
- [Cache Providers](../documents/cache/providers.md)
