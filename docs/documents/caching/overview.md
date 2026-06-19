# Caching Overview

The Schemata caching subsystem provides a unified `ICacheProvider` abstraction over any backing store. Two implementations ship in-tree: `DistributedCacheProvider` (wraps `IDistributedCache`) and `RedisCacheProvider` (wraps StackExchange.Redis). The `Schemata.Entity.Cache` package builds on top of `ICacheProvider` to add transparent query caching and automatic eviction to the repository layer.

## Where the code lives

| Item | Path |
|---|---|
| `ICacheProvider` | `src/Schemata.Caching.Skeleton/ICacheProvider.cs` |
| `CacheEntryOptions` | `src/Schemata.Caching.Skeleton/CacheEntryOptions.cs` |
| `DistributedCacheProvider` | `src/Schemata.Caching.Distributed/DistributedCacheProvider.cs` |
| `IndexLocks` | `src/Schemata.Caching.Distributed/IndexLocks.cs` |
| `RedisCacheProvider` | `src/Schemata.Caching.Redis/RedisCacheProvider.cs` |

## ICacheProvider

```csharp
public interface ICacheProvider
{
    Task<byte[]?>                GetAsync(string key, CancellationToken ct = default);
    Task                         SetAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default);
    Task<bool>                   TryAddAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default);
    Task<bool>                   TryReplaceAsync(string key, byte[] expected, byte[] replacement, CacheEntryOptions options, CancellationToken ct = default);
    Task<bool>                   TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default);
    Task                         RemoveAsync(string key, CancellationToken ct = default);
    Task                         CollectionAddAsync(string key, string member, CacheEntryOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default);
    Task                         CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default);
    Task                         CollectionRemoveAsync(string key, string member, CancellationToken ct = default);
    Task                         CollectionClearAsync(string key, CancellationToken ct = default);
}
```

The interface has two surfaces:

- **Key-value** — `GetAsync`, `SetAsync`, `RemoveAsync` for plain reads and writes over raw byte arrays;
  `TryAddAsync` (atomic insert-if-absent), `TryReplaceAsync` (atomic compare-and-swap), and
  `TryRemoveAsync` (atomic compare-and-delete) for the atomic operations an idempotency or lock pattern
  needs.
- **Collection** — `CollectionAddAsync`, `CollectionMembersAsync`, `CollectionRemoveAsync`,
  `CollectionClearAsync` provide set semantics for consumers that need atomic reverse-index
  bookkeeping.

The atomic operations are where the providers diverge: `RedisCacheProvider` implements all three;
`DistributedCacheProvider` throws `NotSupportedException` for `TryAddAsync`, `TryReplaceAsync`, and
`TryRemoveAsync`, because `IDistributedCache` exposes no atomic compare-and-swap.

## CacheEntryOptions

```csharp
public class CacheEntryOptions
{
    public DateTimeOffset? AbsoluteExpiration              { get; set; }
    public TimeSpan?       AbsoluteExpirationRelativeToNow { get; set; }
    public TimeSpan?       SlidingExpiration               { get; set; }
}
```

All three expiration modes mirror `DistributedCacheEntryOptions`. The Redis provider emulates sliding expiration by storing options in a companion metadata key and refreshing the TTL on each read.

## Provider selection

| Provider | Package | Backend | Collection ops | Multi-process safe |
|---|---|---|---|---|
| `DistributedCacheProvider` | `Schemata.Caching.Distributed` | Any `IDistributedCache` | JSON-serialized `HashSet<string>` with in-process striped locks | No |
| `RedisCacheProvider` | `Schemata.Caching.Redis` | Redis via StackExchange.Redis | Native Redis Set commands (`SADD`, `SMEMBERS`, `SREM`, `DEL`) | Yes |

The key difference is collection safety. `DistributedCacheProvider` uses a 64-stripe in-process `SemaphoreSlim` array (`IndexLocks`) to serialize read-modify-write on collection keys. This prevents lost writes within a single process but does not protect against concurrent writes from multiple processes sharing the same backend. Use `RedisCacheProvider` for multi-process or cluster deployments.

## Layer stack

```text
Consumer (query cache, idempotency, custom)
        |
ICacheProvider
        |
DistributedCacheProvider  OR  RedisCacheProvider
        |
IDistributedCache / Redis
```

## Extension points

- **Custom provider**: implement `ICacheProvider` and register it as a singleton or scoped service.
  Consumers resolve `ICacheProvider` from DI.
- **Custom serializer**: a custom provider can layer compression or alternate serialization on top of
  the byte-array surface without changing the consumer.

## Design rationale

The collection surface exists because reverse-index bookkeeping requires set semantics; a plain
key-value store would force a read-modify-write cycle. `DistributedCacheProvider` guards it with
in-process locks, and `RedisCacheProvider` delegates to native Redis sets.

The atomic compare-and-swap operations are an optional capability. Providers that throw
`NotSupportedException` (as `DistributedCacheProvider` does) still serve any consumer that does not
exercise CAS. Patterns that need atomic reserve-and-swap require `RedisCacheProvider`.

## See also

- [distributed.md](distributed.md) — `DistributedCacheProvider` and `IndexLocks` details
- [redis.md](redis.md) — `RedisCacheProvider` and sliding expiration via metadata key
