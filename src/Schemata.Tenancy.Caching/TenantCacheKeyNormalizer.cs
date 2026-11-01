using System;
using Schemata.Caching.Skeleton;

namespace Schemata.Tenancy.Caching;

/// <summary>
///     Fixed tenant namespace applied to every logical key before backend dispatch. The verified
///     tenant identifier is encoded at construction, so a captured outlet keeps its namespace even
///     when the ambient tenant identity changes or disappears.
/// </summary>
public sealed class TenantCacheKeyNormalizer : ICacheKeyNormalizer
{
    // Fixed-format GUIDs and the reserved host label cannot contain this delimiter.
    private const char Separator = '\x1e';

    private readonly string _prefix;

    public TenantCacheKeyNormalizer(Guid tenantUid) {
        TenantUid = tenantUid;
        _prefix   = tenantUid.ToString("N") + Separator;
    }

    /// <summary>Gets the verified tenant identifier encoded into every normalized key.</summary>
    public Guid TenantUid { get; }

    public string Normalize(string key) {
        return string.Concat(_prefix, key);
    }
}
