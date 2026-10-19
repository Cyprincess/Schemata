using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Caching.Skeleton;

/// <summary>
///     Native storage contract behind the canonical outlet: the full cache contract over
///     already-normalized logical keys plus sliding refresh, implemented by Memory and Redis stores.
/// </summary>
/// <remarks>
///     Deliberately independent of <see cref="ICacheProvider" /> so a native backend can never be
///     resolved as a second consumer outlet; <see cref="CacheProvider" /> is the single execution layer.
/// </remarks>
public interface ICacheBackend
{
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    Task<bool> TryAddAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default);

    Task<bool> TryReplaceAsync(string key, byte[] expected, byte[] replacement, CacheEntryOptions options, CancellationToken ct = default);

    Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default);

    /// <summary>
    ///     Extends the sliding window of an existing entry without rewriting its value or its
    ///     absolute deadline; expired entries are not resurrected.
    /// </summary>
    Task RefreshAsync(string key, CancellationToken ct = default);

    Task CollectionAddAsync(string key, string member, CacheEntryOptions options, CancellationToken ct = default);

    Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default);

    Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default);

    Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default);

    Task CollectionClearAsync(string key, CancellationToken ct = default);
}
