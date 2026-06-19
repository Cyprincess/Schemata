using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Supports soft deletion: entities are marked as deleted and scheduled
///     for eventual permanent removal, following
///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
/// </summary>
public interface ISoftDelete
{
    /// <summary>
    ///     The time at which the entity entered the soft-deleted state.
    ///     A value indicates the entity is logically deleted.
    /// </summary>
    DateTime? DeleteTime { get; set; }

    /// <summary>
    ///     The time scheduled for permanent purge of the soft-deleted entity.
    /// </summary>
    DateTime? PurgeTime { get; set; }
}
