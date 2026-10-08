# Redis Cache Provider

`RedisCacheProvider` is the `ICacheProvider` implementation backed by Redis via StackExchange.Redis. It uses native Redis Set commands for collection membership across application processes. Redis Cluster deployments require each cache key and its companion metadata key to share a hash slot.

## Where the code lives

| Item                 | Path                                               |
| -------------------- | -------------------------------------------------- |
| `RedisCacheProvider` | `src/Schemata.Caching.Redis/RedisCacheProvider.cs` |

## Mechanism

`RedisCacheProvider` holds an `IDatabase` reference obtained from `IConnectionMultiplexer.GetDatabase()` at construction time.

### Key-value operations

`GetAsync` calls `StringGetAsync`. On a non-null result, it calls `RefreshAsync` to extend the sliding expiration window.

`SetAsync` publishes the value, serialized expiration options and their TTLs in one server-side script. `TryAddAsync` checks absence and publishes the same pair in one script; a losing caller changes neither key.

`TryReplaceAsync` (compare-and-swap) and `TryRemoveAsync` (compare-and-delete) run server-side Lua
scripts that read the current value, compare it to the expected bytes, and apply the change only on a
match — value key and metadata key together — returning whether the change occurred. Running the
compare and the write in one script makes each operation atomic across processes.

`RemoveAsync` deletes both the value key and the metadata key in a single multi-key delete command.

### Collection operations

Collection operations use native Redis Set commands:

| Operation                | Redis command |
| ------------------------ | ------------- |
| `CollectionAddAsync`     | `SADD`        |
| `CollectionMembersAsync` | `SMEMBERS`    |
| `CollectionRemoveAsync`  | `SREM`        |
| `CollectionClearAsync`   | `DEL`         |

Collection addition updates members and expiration metadata in one script. Removal executes `SREM` in bounded argument batches and removes metadata only when the set is absent, inside the same script. Redis removes an empty set itself; there is no delayed set deletion that could erase a new member.

### Sliding expiration via metadata key

Redis does not natively support sliding expiration. `RedisCacheProvider` emulates it by storing the serialized `CacheEntryOptions` in a companion key (`key + ":__meta__"`). On every read (`GetAsync`, `CollectionMembersAsync`, `CollectionRemoveAsync`), `RefreshAsync` is called:

1. Reads the metadata key.
2. Deserializes `CacheEntryOptions`.
3. If `SlidingExpiration` is set, computes the new TTL as `min(SlidingExpiration, AbsoluteExpiration - now)`.
4. Runs a script that refreshes both TTLs only if the value still exists and the metadata bytes still match the options read.

The metadata key shares the same TTL as the value key, so both expire together.

## Concurrency safety

Independent processes share Redis's server-side conditional writes. Collection mutation and metadata cleanup execute together. Reads may observe a concurrent replacement; sliding refresh is conditional on the metadata snapshot and cannot apply old expiration options to a differently configured replacement. Corrupt metadata propagates a JSON error to the caller.

**Redis Cluster key requirement:** transactions, Lua scripts, and multi-key deletion require all
participating keys to hash to the same slot. The provider appends `:__meta__` to the supplied key
and does not add a hash tag. Supply a key such as `cache:{entry:42}` so its metadata key
`cache:{entry:42}:__meta__` retains the same nonempty `{entry:42}` tag. Untagged keys do not
guarantee this placement. See the Redis Cluster specification's
[hash-tag rules](https://redis.io/docs/latest/operate/oss_and_stack/reference/cluster-spec/#hash-tags).

Cache and database commits are not atomic together: there is no distributed transaction spanning
Redis and the application database. Consumers can defer cache writes until after the database
transaction commits, but a process crash between commit and the cache write can leave stale cache
entries until TTL expires.

## Registration

```csharp
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
services.AddRedisCache();
```

The extension selects `RedisCacheProvider` as the backend behind the canonical `ICacheProvider` outlet through `AddCacheProvider`. A later explicit provider selection replaces it; see [overview.md](overview.md) for the composition contract.

## Extension points

- **Key prefix**: wrap `RedisCacheProvider` in a decorator that prepends a tenant or environment prefix to all keys.

## Design motivation

Server-side scripts keep conditional comparisons, member mutations and metadata changes within Redis's command execution boundary. The companion metadata key carries the absolute deadline and sliding interval shared by application processes.

## Caveats

- Cache and database are not atomic together. A crash between database commit and cache eviction leaves stale entries until TTL expires. This is an inherent limitation of any cache-aside pattern.
- The metadata key (`key + ":__meta__"`) doubles the number of Redis keys. Plan key eviction policies accordingly.
- Redis Cluster support depends on the shared-slot key requirement above. The provider uses multi-key transactions, Lua scripts, and deletion; automatic single-key routing does not make cross-slot operations valid.

## See also

- [overview.md](overview.md) — `ICacheProvider` abstraction and provider selection
