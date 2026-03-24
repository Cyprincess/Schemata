using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity tracks creation and last-update timestamps.
/// </summary>
public interface ITimestamp
{
    /// <summary>
    ///     Gets or sets the time at which the entity was created.
    /// </summary>
    DateTime? CreateTime { get; set; }

    /// <summary>
    ///     Gets or sets the time at which the entity was last updated.
    /// </summary>
    DateTime? UpdateTime { get; set; }
}
