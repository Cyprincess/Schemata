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
    ///     The time at which the entity was soft-deleted.
    ///     A non-null value indicates the entity is logically deleted.
    /// </summary>
    DateTime? DeleteTime { get; set; }

    /// <summary>
    ///     The time at which the soft-deleted entity will be permanently purged.
    /// </summary>
    DateTime? PurgeTime { get; set; }
}
