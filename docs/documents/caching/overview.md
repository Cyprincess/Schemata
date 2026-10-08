# Caching Overview

`ICacheProvider` is the single cache contract. It combines byte-array reads, writes and removal with conditional operations (insert-if-absent, compare-and-replace, compare-and-remove) and set membership operations. Every framework provider implements the full contract; conditional operations are atomic across every client sharing the provider's backing state.

## Provider selection

| Provider | Package | Coordination scope |
| --- | --- | --- |
| `MemoryCacheProvider` | `Schemata.Caching.Memory` | One shared provider instance in one process |
| `RedisCacheProvider` | `Schemata.Caching.Redis` | Processes sharing the same Redis database and keys |

Query caching, resource idempotency, DPoP replay protection and cache-backed security token slots resolve the same `ICacheProvider` outlet, so their reads, conditional writes and collection mutations act on one backend state. An application's own `IDistributedCache` usage is independent of this contract.

## Registration

For one application process:

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddMemoryCacheProvider();
```

For application processes sharing Redis:

```csharp
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
services.AddRedisCache();
```

Both extensions call `AddCacheProvider`, which installs the backend under the keyed `CacheServiceKeys.Backend` slot and exposes one unkeyed `ICacheProvider` outlet forwarding through `CacheServiceKeys.Selected`. A later explicit selection replaces the earlier backend; repeating the same selection keeps one singleton outlet. Third-party providers register through the same `AddCacheProvider` overloads. Independent extra backends resolve only through their own explicit keys and never enter the canonical outlet. Resolving the outlet without any backend registration throws at first resolution.

`AddCacheProviderWrapper<TWrapper>` selects a wrapper as the public selection; the wrapper receives the backend through `[FromKeyedServices(CacheServiceKeys.Backend)]`. Wrapper and backend installation compose in either order, and repeating the same wrapper replaces the selection instead of stacking. `AddTenantCache` uses this composition.

## Expiration and ownership

`CacheEntryOptions` exposes absolute expiration, relative absolute expiration and sliding expiration. Memory and Redis use the earliest absolute deadline when both absolute forms are supplied, and cap sliding refresh at that deadline.

Memory uses the platform `MemoryCache` for expiration. It copies byte arrays on storage and retrieval so caller mutation cannot bypass conditional writes. Synchronization covers plain mutations, conditional operations and set membership on the owned cache.

Redis stores serialized expiration options under `key + ":__meta__"`. Server-side scripts publish values or members, metadata and TTLs together. Conditional writes compare actual stored bytes. See [Redis](redis.md) for key-slot and deployment constraints.

## Query generations

Query caching initializes an absent entity generation atomically, then reads the stored winner. A successful repository commit publishes a new generation. An already-running query fills only its captured generation, so a late result cannot populate the new generation. Database commits and cache publication remain separate operations.

## Source map

| Contract or implementation | Source |
| --- | --- |
| Full cache contract | `src/Schemata.Caching.Skeleton/ICacheProvider.cs` |
| Registration composition | `src/Schemata.Caching.Skeleton/CacheServiceCollectionExtensions.cs`, `src/Schemata.Caching.Skeleton/CacheServiceKeys.cs` |
| Memory implementation | `src/Schemata.Caching.Memory/MemoryCacheProvider.cs` |
| Redis implementation | `src/Schemata.Caching.Redis/RedisCacheProvider.cs` |
| Generation ownership | `src/Schemata.Entity.Cache/CacheGeneration.cs` |

## See also

- [Redis provider](redis.md)
- [Query cache](../entity/query-cache.md)
