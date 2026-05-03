using System;

namespace Schemata.Caching.Skeleton;

/// <summary>Expiration policy for a cache entry.</summary>
public class CacheEntryOptions
{
    /// <summary>Absolute expiration as a specific point in time.</summary>
    public DateTimeOffset? AbsoluteExpiration { get; set; }

    /// <summary>Absolute expiration relative to the moment the entry is stored.</summary>
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }

    /// <summary>Sliding expiration; extends lifetime on each access.</summary>
    public TimeSpan? SlidingExpiration { get; set; }
}
