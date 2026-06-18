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
    ///     value is missing, blank, or not a well-formed <see cref="Guid" />. Callers that treat an absent
    ///     source as "no tenant" must short-circuit before calling this.
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
