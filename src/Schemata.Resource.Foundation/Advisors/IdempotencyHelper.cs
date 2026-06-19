using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Caching.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Shared cache, caller, and payload helpers for request idempotency advisors.
/// </summary>
internal static class IdempotencyHelper
{
    /// <summary>Resolves the resource options from the advisor pipeline's service provider, falling back to defaults.</summary>
    public static SchemataResourceOptions ResolveOptions(IServiceProvider sp) {
        return sp.GetService<IOptions<SchemataResourceOptions>>()?.Value ?? new SchemataResourceOptions();
    }

    /// <summary>
    ///     Reads the cache entry under <paramref name="key" /> and, when it is a finalized
    ///     (DONE) envelope, returns its payload. A payload-hash mismatch raises
    ///     <see cref="ConcurrencyException" /> (same request id, different payload). A missing
    ///     entry or a still-pending reservation yields <c>(false, null)</c>.
    /// </summary>
    public static async Task<(bool Found, TPayload? Payload)> ReadDoneAsync<TPayload>(
        ICacheProvider    cache,
        string            key,
        string            payloadHash,
        CancellationToken ct
    )
        where TPayload : class {
        var bytes = await cache.GetAsync(key, ct);
        if (bytes is null) {
            return (false, null);
        }

        if (JsonSerializer.Deserialize<IdempotencyHeader>(bytes)?.Kind != IdempotencyKind.Done) {
            return (false, null);
        }

        var cached = JsonSerializer.Deserialize<IdempotencyEnvelope<TPayload>>(bytes);
        if (cached is null) {
            return (false, null);
        }

        if (cached.Hash != payloadHash) {
            throw new ConcurrencyException();
        }

        return (true, cached.Payload);
    }

    /// <summary>
    ///     Polls for a finalized (DONE) envelope under <paramref name="key" /> until it appears or
    ///     <paramref name="wait" /> elapses, used when a replay finds another attempt's reservation.
    /// </summary>
    public static async Task<TPayload?> AwaitDoneAsync<TPayload>(
        ICacheProvider    cache,
        string            key,
        string            payloadHash,
        TimeSpan          wait,
        TimeProvider      time,
        CancellationToken ct
    )
        where TPayload : class {
        var deadline = time.GetUtcNow() + wait;
        while (true) {
            var (found, payload) = await ReadDoneAsync<TPayload>(cache, key, payloadHash, ct);
            if (found) {
                return payload;
            }

            if (time.GetUtcNow() >= deadline) {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), time, ct);
        }
    }

    /// <summary>
    ///     Derives a stable caller identifier partitioning the idempotency cache
    ///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>:
    ///     a cached result is only replayed to the caller that produced it.
    /// </summary>
    public static string PrincipalId(ClaimsPrincipal? principal) {
        return principal?.FindFirst(Claims.Subject)?.Value
            ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal?.Identity?.Name
            ?? Principals.Anonymous;
    }

    /// <summary>
    ///     Hashes the canonical JSON of the request so a replayed
    ///     <c>request_id</c> with a different payload is rejected instead of
    ///     served another request's cached result.
    /// </summary>
    public static string HashPayload<TRequest>(TRequest request) {
        var json = JsonSerializer.SerializeToUtf8Bytes(request);
        return Convert.ToHexString(SHA256.HashData(json));
    }
}
