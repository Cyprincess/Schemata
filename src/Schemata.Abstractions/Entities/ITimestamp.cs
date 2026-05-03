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
    ///     The time at which the entity was created.
    /// </summary>
    DateTime? CreateTime { get; set; }

    /// <summary>
    ///     The time at which the entity was last updated.
    /// </summary>
    DateTime? UpdateTime { get; set; }
}
