# Distributed Cache Provider

`DistributedCacheProvider` is the `ICacheProvider` implementation backed by any `IDistributedCache` (in-memory, SQL Server, Redis via the Microsoft adapter, etc.). It is the default choice for single-process deployments.

## Where the code lives

| Item | Path |
|---|---|
| `DistributedCacheProvider` | `src/Schemata.Caching.Distributed/DistributedCacheProvider.cs` |
| `IndexLocks` | `src/Schemata.Caching.Distributed/IndexLocks.cs` |

## Mechanism

Key-value operations (`GetAsync`, `SetAsync`, `RemoveAsync`) delegate directly to the underlying `IDistributedCache`, mapping `CacheEntryOptions` onto `DistributedCacheEntryOptions`. An absolute-relative expiration is normalized against the injected `TimeProvider` before storage.

Collection operations (`CollectionAddAsync`, `CollectionMembersAsync`, `CollectionRemoveAsync`, `CollectionClearAsync`) simulate set semantics by serializing a `HashSet<string>` as JSON and storing it under the collection key. Because `IDistributedCache` does not expose atomic read-modify-write operations, a striped in-process lock (`IndexLocks`) serializes concurrent access to the same collection key.

### IndexLocks

`IndexLocks` maintains 64 `SemaphoreSlim` instances. A collection key is mapped to a stripe by `(uint)key.GetHashCode() % 64`. Before any read-modify-write on a collection, the provider acquires the semaphore for that stripe and releases it when the operation completes.

```csharp
using var _ = await IndexLocks.AcquireAsync(key, ct);
var payload = await ReadSetAsync(key, ct);
// ... modify ...
await WriteSetAsync(key, payload.Members, options, ct);
```

The stripe count is fixed at 64 regardless of the number of distinct keys, so memory stays constant. Unrelated keys may share a stripe and serialize incidentally; this is intentional — the lock exists to prevent lost writes, not to maximize throughput.

## Concurrency safety

**Scope: single-process.** `IndexLocks` provides in-process serialization for collection
operations. When multiple processes share the same `IDistributedCache` backend (a shared SQL Server
cache, or a Redis instance accessed via the `IDistributedCache` adapter), concurrent collection
writes from different processes are not serialized and can produce lost updates.

For multi-process or cluster deployments, use `RedisCacheProvider`. It uses native Redis Set
commands (`SADD`, `SMEMBERS`, `SREM`, `DEL`) which are atomic at the Redis server level.

## Unsupported atomic operations

`TryAddAsync`, `TryReplaceAsync`, and `TryRemoveAsync` throw `NotSupportedException`. `IDistributedCache`
exposes no atomic compare-and-swap, and emulating one with the in-process lock would still be unsafe
across processes sharing the backend. Patterns that need atomic reserve-and-swap require
`RedisCacheProvider`.

## Extension points

- **Custom `IDistributedCache`**: register any `IDistributedCache` implementation (e.g., `AddStackExchangeRedisCache`, `AddSqlServerCache`) and `DistributedCacheProvider` will use it.
- **Replacing the provider**: register a custom `ICacheProvider` implementation before `DistributedCacheProvider` to override it for all cache operations.

## Registration

```csharp
services.AddDistributedMemoryCache();
services.AddSingleton<ICacheProvider, DistributedCacheProvider>();
```

Or use the extension method from `Schemata.Caching.Distributed`:

```csharp
services.AddDistributedMemoryCache();
services.AddDistributedCache();
```

## Design motivation

`DistributedCacheProvider` exists to make the caching subsystem usable without Redis. Any `IDistributedCache` backend works, including the in-memory implementation for development and testing. The in-process lock is a pragmatic trade-off: it prevents lost writes in the common single-process case without requiring a distributed lock service.

## Caveats

- Collection operations are single-process safe. Multi-process deployments sharing the same backend are subject to lost-write races on collection keys; use `RedisCacheProvider` for cluster-safe collection operations.
- The atomic operations (`TryAddAsync`, `TryReplaceAsync`, `TryRemoveAsync`) throw `NotSupportedException`. Use `RedisCacheProvider` when a pattern needs them.
- JSON serialization of `HashSet<string>` adds overhead compared to native set commands. For high-throughput eviction workloads, prefer `RedisCacheProvider`.

## See also

- [overview.md](overview.md) — `ICacheProvider` abstraction and provider selection
- [redis.md](redis.md) — `RedisCacheProvider` for cluster-safe collection operations
