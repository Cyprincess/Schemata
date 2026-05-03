using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Stores and retrieves idempotency keys for create operations
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>, preventing
///     duplicate processing of the same client request.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    ///     Retrieves a previously stored result for the given request identifier.
    /// </summary>
    /// <typeparam name="T">The type of the stored result.</typeparam>
    /// <param name="requestId">The unique client-supplied request identifier.</param>
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
    /// <returns>The stored result, or <see langword="null" /> if not found.</returns>
    Task<T?> GetAsync<T>(string requestId, CancellationToken ct = default);

    /// <summary>
    ///     Stores a result for the given request identifier with an optional expiry.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="requestId">The unique client-supplied request identifier.</param>
    /// <param name="value">The result to store.</param>
    /// <param name="expiry">Optional time-to-live; defaults to 24 hours when <see langword="null" />.</param>
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
    Task SetAsync<T>(
        string            requestId,
        T                 value,
        TimeSpan?         expiry = null,
        CancellationToken ct     = default
    );
}
