using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Caching.Skeleton;

/// <summary>
///     Full cache contract: byte-array reads, writes and removal, conditional writes atomic across
///     every client sharing the provider's backing state, sliding refresh, and set membership
///     operations.
/// </summary>
/// <remarks>Distributed implementations must provide server-side atomicity across processes.</remarks>
public interface ICacheProvider
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
