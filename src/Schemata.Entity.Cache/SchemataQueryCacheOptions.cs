using System;

namespace Schemata.Entity.Cache;

/// <summary>Options for the Schemata query cache.</summary>
public class SchemataQueryCacheOptions
{
    /// <summary>
    ///     Sliding expiration applied to cached query results and reverse-index entries.
    ///     Defaults to 5 minutes.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     When <see langword="false" />, the update and remove advisors skip cache eviction.
    ///     The query and result advisors remain active; entries live until TTL expires.
    /// </summary>
    public bool EvictionEnabled { get; set; } = true;
}
