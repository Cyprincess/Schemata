using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

internal static class IdempotencyHelper
{
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
