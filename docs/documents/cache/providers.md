# Cache Providers

Schemata ships two `ICacheProvider` implementations. Choose based on your deployment topology: single-process or multi-process.

## DistributedCacheProvider

| Package                    | Dependency                      |
| -------------------------- | ------------------------------- |
| `Schemata.Caching.Distributed` | `Schemata.Caching.Skeleton`, `Microsoft.Extensions.Caching.Abstractions` |

`DistributedCacheProvider` wraps any `IDistributedCache` implementation — in-memory (`AddDistributedMemoryCache`), SQL Server, or custom backends. It acts as an adapter, translating `ICacheProvider` calls to `IDistributedCache` operations.

**Key-value operations:** Direct pass-through to `IDistributedCache`.

**Collection operations:** Simulate set semantics by serializing a `HashSet<string>` as JSON in a single cache entry:

```
key → {"Members":["abc","def"],"Options":{...}}
```

Read-modify-write cycles (add member, remove member) are protected by a **striped in-process lock** (`IndexLocks` — 64 `SemaphoreSlim` slots keyed by hash) to prevent lost writes within a single process.

**Limitation:** The lock is in-process only. In multi-process deployments sharing the same `IDistributedCache` backend, concurrent collection mutations from different processes can still race. Use the Redis provider for multi-process scenarios.

**Registration:**

```csharp
// In-memory
services.AddDistributedMemoryCache();
services.AddDistributedCache();

// SQL Server
services.AddDistributedSqlServerCache(options => {
    options.ConnectionString = connectionString;
    options.SchemaName = "dbo";
    options.TableName = "Cache";
});
services.AddDistributedCache();
```

The provider is registered as a **singleton**.

## RedisCacheProvider

| Package                 | Dependency                                      |
| ----------------------- | ----------------------------------------------- |
| `Schemata.Caching.Redis` | `Schemata.Caching.Skeleton`, `StackExchange.Redis` |

`RedisCacheProvider` uses native Redis commands via `StackExchange.Redis`. All mutations operate within Redis transactions (`MULTI`/`EXEC`) for atomicity.

**Key-value operations:** `GET` / `SET` with automatic key expiry.

**Collection operations:** Native Redis Set commands — `SADD`, `SMEMBERS`, `SREM`, `DEL`. These are inherently cluster-safe and require no in-process locking.

**Sliding expiration:** Redis does not natively support sliding expiration on individual keys. The provider emulates it by storing a companion metadata key (`{key}:__meta__`) containing the serialized `CacheEntryOptions` as JSON. On every read (`GetAsync`, `CollectionMembersAsync`), if `SlidingExpiration` is configured, the provider re-issues `EXPIRE` on both the data key and the metadata key.

**Registration:**

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddRedisCache();
```

The multiplexer and the provider are both registered as **singletons**.

## Comparison

| Aspect                    | DistributedCacheProvider                                | RedisCacheProvider                        |
| ------------------------- | ------------------------------------------------------- | ----------------------------------------- |
| **Backend**               | Any `IDistributedCache`                                 | Redis via `StackExchange.Redis`           |
| **Key-value performance** | Pass-through to backend                                 | Native Redis `GET`/`SET`                  |
| **Collection mechanism**  | JSON `HashSet<string>` with in-process lock             | Native Redis `SADD`/`SMEMBERS`/`SREM`     |
| **Multi-process safety**  | Single-process only (in-process lock)                   | Cluster-safe (native commands)            |
| **Sliding expiration**    | Delegated to backend (if supported)                     | Emulated via companion `__meta__` keys    |
| **Transaction support**   | N/A (lock-based read-modify-write)                      | `MULTI`/`EXEC` transactions              |
| **Typical use**           | Development, single-instance deployments                | Production, multi-replica deployments     |
