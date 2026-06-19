using System;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Tenancy.Foundation.Resolvers;

/// <summary>
///     Shared parsing for the tenant-identifier resolvers that read a raw string from the request
///     (header, query, route, or claim) and expect a <see cref="Guid" />.
/// </summary>
internal static class TenantId
{
    /// <summary>
    ///     Parses an extracted tenant identifier, throwing <see cref="TenantResolveException" /> when the
    ///     value is missing, blank, or malformed. Callers should skip parsing when the request source is absent.
    /// </summary>
    /// <param name="value">The raw identifier extracted from the request.</param>
    /// <returns>The parsed tenant identifier.</returns>
    public static Guid Parse(string? value) {
        if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, null, out var key)) {
            return key;
        }

        throw new TenantResolveException();
    }
}
