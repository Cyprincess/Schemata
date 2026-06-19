using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Response returned by AIP-165 purge dispatch and stored as the operation result.
/// </summary>
/// <remarks>
///     <see cref="Operation" /> references the Scheduling bridge operation resource. Preview
///     executions populate <see cref="PurgeCount" /> and a capped <see cref="PurgeSample" />;
///     force executions populate only the count.
/// </remarks>
/// <seealso href="https://google.aip.dev/165">AIP-165: Purge</seealso>
public sealed class PurgeResponse : ICanonicalName
{
    /// <summary>Canonical name of the addressable long-running operation.</summary>
    public string? Operation { get; set; }

    /// <summary>Number of resources matched by the purge filter.</summary>
    public long PurgeCount { get; set; }

    /// <summary>Preview sample of matching canonical names, capped at 100.</summary>
    public IList<string> PurgeSample { get; set; } = [];

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }
}
