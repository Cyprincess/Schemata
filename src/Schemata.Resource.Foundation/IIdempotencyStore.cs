using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Stores and retrieves idempotency keys to prevent duplicate request processing.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    ///     Retrieves a previously stored result for the given request identifier.
    /// </summary>
    /// <typeparam name="T">The type of the stored result.</typeparam>
    /// <param name="requestId">The unique request identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The stored result, or <see langword="null" /> if not found.</returns>
    Task<T?> GetAsync<T>(string requestId, CancellationToken ct = default);

    /// <summary>
    ///     Stores a result for the given request identifier with an optional expiry.
    /// </summary>
    /// <typeparam name="T">The type of the result to store.</typeparam>
    /// <param name="requestId">The unique request identifier.</param>
    /// <param name="value">The result to store.</param>
    /// <param name="expiry">Optional time-to-live for the stored entry.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SetAsync<T>(
        string            requestId,
        T                 value,
        TimeSpan?         expiry = null,
        CancellationToken ct     = default
    );
}
