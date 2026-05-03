using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Supports optimistic concurrency control via a version token, used by the
///     <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>
///     etag pattern.
/// </summary>
public interface IConcurrency
{
    /// <summary>
    ///     The concurrency version token for optimistic locking.
    /// </summary>
    Guid? Timestamp { get; set; }
}
