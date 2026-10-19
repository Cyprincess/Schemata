using System;

namespace Schemata.Caching.Skeleton;

/// <summary>
///     Optional capability of a cache backend whose operations complete without asynchronous I/O:
///     the synchronous half of the cache contract.
/// </summary>
/// <remarks>
///     `MemoryCacheBackend` implements this interface over in-process state; a network backend stays
///     on <see cref="ICacheBackend" /> alone. The tenancy cache builder binds its own implementation
///     for each tenant outlet, so resolving the interface from a tenant scope reports that the
///     outlet binding exists, not that the selected backend supports synchronous access; presence
///     is not a support probe. Such a binding rejects each operation with
///     <see cref="NotSupportedException" /> when the backend performs asynchronous I/O.
///     Keys are the caller's logical keys; the bound implementation applies its own normalization
///     before backend dispatch. Implementations must not wait on asynchronous work.
/// </remarks>
public interface ISynchronousCacheBackend
{
    byte[]? Get(string key);

    void Set(string key, byte[] value, CacheEntryOptions options);

    void Refresh(string key);

    void Remove(string key);
}
