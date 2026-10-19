using System;

namespace Schemata.Entity.Cache;

/// <summary>Options for the Schemata query cache.</summary>
public class SchemataQueryCacheOptions
{
    /// <summary>
    ///     Absolute expiration applied to cached query results. Generation metadata does not expire.
    ///     Defaults to 5 minutes.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);
}
