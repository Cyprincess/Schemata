using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace Schemata.Resource.Foundation;

/// <summary>
///     <see cref="IDistributedCache" />-backed implementation of <see cref="IIdempotencyStore" />
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
///     Stores results as JSON with a default 24-hour TTL.
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);

    private readonly IDistributedCache _cache;

    /// <summary>
    ///     Initializes a new instance backed by the specified distributed cache.
    /// </summary>
    /// <param name="cache">The <see cref="IDistributedCache" /> implementation.</param>
    public IdempotencyStore(IDistributedCache cache) { _cache = cache; }

    #region IIdempotencyStore Members

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string requestId, CancellationToken ct = default) {
        var bytes = await _cache.GetAsync(requestId, ct);
        if (bytes is null) {
            return default;
        }

        return JsonSerializer.Deserialize<T>(bytes);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(
        string            requestId,
        T                 value,
        TimeSpan?         expiry = null,
        CancellationToken ct     = default
    ) {
        var bytes   = JsonSerializer.SerializeToUtf8Bytes(value);
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiry ?? DefaultExpiry };

        await _cache.SetAsync(requestId, bytes, options, ct);
    }

    #endregion
}
