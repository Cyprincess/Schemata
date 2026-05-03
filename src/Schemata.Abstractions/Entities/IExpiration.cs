using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity can expire at a scheduled time, corresponding
///     to <seealso href="https://google.aip.dev/214">AIP-214: Resource expiration</seealso>
///     <c>expire_time</c>.
/// </summary>
public interface IExpiration
{
    /// <summary>
    ///     The time at which the entity expires and should be considered
    ///     unavailable.
    /// </summary>
    DateTime? ExpireTime { get; set; }
}
