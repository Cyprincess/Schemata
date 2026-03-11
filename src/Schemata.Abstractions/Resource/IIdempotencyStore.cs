using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Abstractions.Resource;

public interface IIdempotencyStore
{
    Task<T?> GetAsync<T>(string requestId, CancellationToken ct = default);

    Task SetAsync<T>(
        string            requestId,
        T                 value,
        TimeSpan?         expiry = null,
        CancellationToken ct     = default
    );
}
