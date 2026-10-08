using System;
using Schemata.Abstractions.Tenancy;

namespace Schemata.Tenancy.Caching;

internal static class TenantCacheKey
{
    // Fixed-format GUIDs and the reserved host label cannot contain this delimiter.
    private const char Separator = '\x1e';

    internal static string Frame(string key) {
        ArgumentNullException.ThrowIfNull(key);

        var identity = TenantContext.Current;
        return identity.Uid is { } uid
            ? string.Concat(uid.ToString("N"), Separator, key)
            : string.Concat("host", Separator, key);
    }
}