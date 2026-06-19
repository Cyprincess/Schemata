# Redis Cache Provider

`RedisCacheProvider` is the `ICacheProvider` implementation backed by Redis via StackExchange.Redis. It is the correct choice for multi-process and cluster deployments because its collection operations use native Redis Set commands, which are atomic at the Redis server level.

## Where the code lives

| Item | Path |
|---|---|
| `RedisCacheProvider` | `src/Schemata.Caching.Redis/RedisCacheProvider.cs` |

## Mechanism

`RedisCacheProvider` holds an `IDatabase` reference obtained from `IConnectionMultiplexer.GetDatabase()` at construction time.

### Key-value operations

`GetAsync` calls `StringGetAsync`. On a non-null result, it calls `RefreshAsync` to extend the sliding expiration window.

`SetAsync` uses a Redis transaction (`IDatabase.CreateTransaction()`) to atomically set the value and its expiration, then store the serialized `CacheEntryOptions` in a companion metadata key (`key + ":__meta__"`).

`TryAddAsync` uses `StringSetAsync(key, value, expiry, When.NotExists)` — a single-round-trip atomic insert-if-absent (`SET key value EX <expiry> NX`).

`TryReplaceAsync` (compare-and-swap) and `TryRemoveAsync` (compare-and-delete) run server-side Lua
scripts that read the current value, compare it to the expected bytes, and apply the change only on a
match — value key and metadata key together — returning whether the change occurred. Running the
compare and the write in one script makes each operation atomic across processes.

`RemoveAsync` deletes both the value key and the metadata key in a transaction.

### Collection operations

Collection operations use native Redis Set commands:

| Operation | Redis command |
|---|---|
| `CollectionAddAsync` | `SADD` |
| `CollectionMembersAsync` | `SMEMBERS` |
| `CollectionRemoveAsync` | `SREM` |
| `CollectionClearAsync` | `DEL` |

These commands are atomic at the Redis server level, making collection operations safe across multiple processes and cluster nodes. No in-process locking is needed.

### Sliding expiration via metadata key

Redis does not natively support sliding expiration. `RedisCacheProvider` emulates it by storing the serialized `CacheEntryOptions` in a companion key (`key + ":__meta__"`). On every read (`GetAsync`, `CollectionMembersAsync`, `CollectionRemoveAsync`), `RefreshAsync` is called:

1. Reads the metadata key.
2. Deserializes `CacheEntryOptions`.
3. If `SlidingExpiration` is set, computes the new TTL as `min(SlidingExpiration, AbsoluteExpiration - now)`.
4. Calls `KeyExpireAsync` on both the value key and the metadata key in a transaction.

The metadata key shares the same TTL as the value key, so both expire together.

## Concurrency safety

**Scope: cluster-safe.** Collection operations are atomic at the Redis server level (`SADD`,
`SMEMBERS`, `SREM`, `DEL`); multiple application instances can add and remove members from the
same set concurrently. Key-value operations are also cluster-safe: `TryAddAsync` uses `SET NX`,
and the compare-and-swap operations run server-side Lua scripts.

Cache and database commits are not atomic together: there is no distributed transaction spanning
Redis and the application database. Consumers can defer cache writes until after the database
transaction commits, but a process crash between commit and the cache write can leave stale cache
entries until TTL expires.

## Registration

```csharp
services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
services.AddSingleton<ICacheProvider, RedisCacheProvider>();
```

Or use the extension method from `Schemata.Caching.Redis`:

```csharp
services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
services.AddRedisCache();
```

## Extension points

- **Custom database selection**: subclass `RedisCacheProvider` and override the constructor to call `multiplexer.GetDatabase(dbIndex)` for a specific database number.
- **Key prefix**: wrap `RedisCacheProvider` in a decorator that prepends a tenant or environment prefix to all keys.

## Design motivation

Using native Redis Set commands for collection operations eliminates the read-modify-write race that affects `DistributedCacheProvider`. The metadata key pattern for sliding expiration is a pragmatic workaround for Redis's lack of native sliding TTL support; it adds one extra key per cached entry. The atomic compare-and-swap operations use Lua scripts because they need to read, compare, and write the value and metadata keys as a single server-side step.

## Caveats

- Cache and database are not atomic together. A crash between database commit and cache eviction leaves stale entries until TTL expires. This is an inherent limitation of any cache-aside pattern.
- The metadata key (`key + ":__meta__"`) doubles the number of Redis keys. Plan key eviction policies accordingly.
- `RedisCacheProvider` uses `IDatabase`, not `IServer`. Cluster-mode Redis routes commands by key slot; all operations on a single key are routed to the correct node automatically. Cross-slot operations (e.g., multi-key transactions) are not used.

## See also

- [overview.md](overview.md) — `ICacheProvider` abstraction and provider selection
- [distributed.md](distributed.md) — `DistributedCacheProvider` for single-process deployments
