using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation;

public sealed class IdempotencyStore : IIdempotencyStore
{
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);

    private readonly IDistributedCache _cache;

    public IdempotencyStore(IDistributedCache cache) { _cache = cache; }

    #region IIdempotencyStore Members

    public async Task<T?> GetAsync<T>(string requestId, CancellationToken ct = default) {
        var bytes = await _cache.GetAsync(requestId, ct);
        if (bytes is null) {
            return default;
        }

        return JsonSerializer.Deserialize<T>(bytes);
    }

    public async Task SetAsync<T>(
        string            requestId,
        T                 value,
        TimeSpan?         expiry = null,
        CancellationToken ct     = default
    ) {
        var bytes   = JsonSerializer.SerializeToUtf8Bytes(value);
        var options = new DistributedCacheEntryOptions {
            AbsoluteExpirationRelativeToNow = expiry ?? DefaultExpiry
        };

        await _cache.SetAsync(requestId, bytes, options, ct);
    }

    #endregion
}
