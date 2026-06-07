# Distributed Cache Provider

`DistributedCacheProvider` is the `ICacheProvider` implementation backed by any `IDistributedCache` (in-memory, SQL Server, Redis via the Microsoft adapter, etc.). It is the default choice for single-process deployments.

## Where the code lives

| Item | Path |
|---|---|
| `DistributedCacheProvider` | `src/Schemata.Caching.Distributed/DistributedCacheProvider.cs` |
| `IndexLocks` | `src/Schemata.Caching.Distributed/IndexLocks.cs` |

## Mechanism

Key-value operations (`GetAsync`, `SetAsync`, `TryAddAsync`, `RemoveAsync`) delegate directly to the underlying `IDistributedCache`. No additional logic is applied.

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

## Single-process safety only

`IndexLocks` provides in-process serialization only. When multiple processes share the same `IDistributedCache` backend (e.g., a shared SQL Server cache or a shared Redis instance accessed via the `IDistributedCache` adapter), concurrent collection writes from different processes are not serialized and can produce lost updates.

For multi-process or cluster deployments, use `RedisCacheProvider` instead. It uses native Redis Set commands (`SADD`, `SMEMBERS`, `SREM`, `DEL`) which are atomic at the Redis server level.

## TryAddAsync

`TryAddAsync` acquires the in-process lock for the key, reads the existing value, and returns `false` if a non-empty value already exists. If absent, it writes the new value and returns `true`. This provides insert-if-absent semantics within a single process.

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

- Collection operations are single-process safe only. Multi-process deployments sharing the same backend are subject to lost-write races on collection keys. Use `RedisCacheProvider` for cluster-safe collection operations.
- `TryAddAsync` is also single-process safe only for the same reason.
- The JSON serialization of `HashSet<string>` adds overhead compared to native set commands. For high-throughput eviction workloads, prefer `RedisCacheProvider`.

## See also

- [overview.md](overview.md) — `ICacheProvider` abstraction and provider selection
- [redis.md](redis.md) — `RedisCacheProvider` for cluster-safe collection operations
- [query-cache.md](query-cache.md) — how collection operations are used by the reverse index
