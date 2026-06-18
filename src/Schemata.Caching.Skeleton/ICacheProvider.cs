using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Caching.Skeleton;

/// <summary>
///     Unified cache abstraction supporting both key-value storage and collection (set) semantics.
/// </summary>
public interface ICacheProvider
{
    /// <summary>Retrieves the raw byte array stored under <paramref name="key" />, or <see langword="null" /> if absent.</summary>
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Stores <paramref name="value" /> under <paramref name="key" /> with the specified expiration options.</summary>
    Task SetAsync(
        string            key,
        byte[]            value,
        CacheEntryOptions options,
        CancellationToken ct = default
    );

    /// <summary>
    ///     Atomically stores <paramref name="value" /> under <paramref name="key" /> only when the
    ///     key does not already exist. Returns <see langword="true" /> when the value was inserted
    ///     and <see langword="false" /> when an entry already lived under the key.
    /// </summary>
    Task<bool> TryAddAsync(
        string            key,
        byte[]            value,
        CacheEntryOptions options,
        CancellationToken ct = default
    );

    /// <summary>
    ///     Atomically replaces the value under <paramref name="key" /> with
    ///     <paramref name="replacement" /> only when the current value equals
    ///     <paramref name="expected" />. Returns <see langword="true" /> when the swap occurred
    ///     and <see langword="false" /> when the current value differed or the key was absent.
    /// </summary>
    /// <remarks>The comparison and swap MUST be a single atomic operation across all processes.</remarks>
    Task<bool> TryReplaceAsync(
        string            key,
        byte[]            expected,
        byte[]            replacement,
        CacheEntryOptions options,
        CancellationToken ct = default
    );

    /// <summary>
    ///     Atomically removes the entry under <paramref name="key" /> only when its current value
    ///     equals <paramref name="expected" />. Returns <see langword="true" /> when the entry was
    ///     removed and <see langword="false" /> when the current value differed or the key was absent.
    /// </summary>
    /// <remarks>The comparison and delete MUST be a single atomic operation across all processes.</remarks>
    Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default);

    /// <summary>Removes the entry (key-value or collection) stored under <paramref name="key" />.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Adds <paramref name="member" /> to the collection stored under <paramref name="key" />.</summary>
    /// <remarks>Creates the collection if it does not already exist.</remarks>
    Task CollectionAddAsync(
        string            key,
        string            member,
        CacheEntryOptions options,
        CancellationToken ct = default
    );

    /// <summary>Returns all members of the collection stored under <paramref name="key" />.</summary>
    Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default);

    /// <summary>Removes <paramref name="members" /> from the collection stored under <paramref name="key" />.</summary>
    Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default);

    /// <summary>Removes <paramref name="member" /> from the collection stored under <paramref name="key" />.</summary>
    Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default);

    /// <summary>Removes the entire collection stored under <paramref name="key" />.</summary>
    Task CollectionClearAsync(string key, CancellationToken ct = default);
}
