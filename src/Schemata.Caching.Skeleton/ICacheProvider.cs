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
