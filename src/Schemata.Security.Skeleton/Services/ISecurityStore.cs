using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Security.Skeleton.Services;

/// <summary>
///     Storage and query contract for security material rows. Hosts may swap the
///     implementation; the default ships in the Foundation package.
/// </summary>
/// <typeparam name="TSecurity">Concrete security entity type.</typeparam>
public interface ISecurityStore<TSecurity> where TSecurity : SchemataSecurity
{
    /// <summary>Finds a row by its canonical name.</summary>
    /// <param name="canonicalName">Canonical name of the row (e.g., <c>securities/{security}</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The row, or <see langword="null" /> when not found.</returns>
    Task<TSecurity?> FindByCanonicalNameAsync(string? canonicalName, CancellationToken ct = default);

    /// <summary>
    ///     Lists rows under a parent, filtered by kind, usage, and status
    ///     (<see langword="null" /> filters are ignored).
    /// </summary>
    /// <param name="parent">Parent canonical name to list under.</param>
    /// <param name="kind">Kind filter; <see langword="null" /> for all.</param>
    /// <param name="usage">Usage filter; <see langword="null" /> for all.</param>
    /// <param name="status">Status filter; <see langword="null" /> for all.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     Rows ordered by create time descending, name ascending as tiebreak under the
    ///     store's collation. Consumers rely on this ordering.
    /// </returns>
    IAsyncEnumerable<TSecurity> ListByParentAsync(string? parent, string? kind = null, string? usage = null,
        string? status = null, CancellationToken ct = default);

    /// <summary>Persists a new row.</summary>
    /// <param name="security">Row to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created row, or <see langword="null" /> when creation failed.</returns>
    Task<TSecurity?> CreateAsync(TSecurity? security, CancellationToken ct = default);

    /// <summary>Persists changes to an existing row.</summary>
    /// <param name="security">Row to update.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(TSecurity? security, CancellationToken ct = default);

    /// <summary>Removes a row.</summary>
    /// <param name="security">Row to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(TSecurity? security, CancellationToken ct = default);
}
