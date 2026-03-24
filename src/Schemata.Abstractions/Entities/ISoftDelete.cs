using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity supports soft deletion with a scheduled purge time.
/// </summary>
public interface ISoftDelete
{
    /// <summary>
    ///     Gets or sets the time at which the entity was soft-deleted.
    /// </summary>
    DateTime? DeleteTime { get; set; }

    /// <summary>
    ///     Gets or sets the time at which the soft-deleted entity will be permanently purged.
    /// </summary>
    DateTime? PurgeTime { get; set; }
}
