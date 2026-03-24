using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity supports optimistic concurrency control via a version token.
/// </summary>
public interface IConcurrency
{
    /// <summary>
    ///     Gets or sets the concurrency timestamp used for optimistic locking.
    /// </summary>
    Guid? Timestamp { get; set; }
}
