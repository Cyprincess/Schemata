using System;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>Retrieval and cache bounds for external authorization request objects.</summary>
public sealed class ExternalRequestObjectOptions
{
    /// <summary>Maximum time for one retrieval.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum response body size in bytes.</summary>
    public int MaximumBodyBytes { get; set; } = 64 * 1024;

    /// <summary>Lifetime of a metadata-version-and-URI-bound cached response.</summary>
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromMinutes(5);
}
