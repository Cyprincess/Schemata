using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Records creation and last-update timestamps, corresponding to
///     <seealso href="https://google.aip.dev/148">AIP-148: Standard fields</seealso>
///     <c>create_time</c> and <c>update_time</c>.
/// </summary>
public interface ITimestamp
{
    /// <summary>
    ///     The entity creation time.
    /// </summary>
    DateTime? CreateTime { get; set; }

    /// <summary>
    ///     The most recent entity update time.
    /// </summary>
    DateTime? UpdateTime { get; set; }
}
