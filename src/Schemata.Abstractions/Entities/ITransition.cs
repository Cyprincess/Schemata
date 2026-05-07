using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Records an audit event or state-change log entry capturing who
///     performed what action.
///     Combine with <see cref="ITimestamp" /> for a complete audit record.
/// </summary>
public interface ITransition
{
    /// <summary>
    ///     The event type identifier
    ///     (e.g., <c>"created"</c>, <c>"updated"</c>, <c>"deleted"</c>).
    /// </summary>
    string Event { get; set; }

    /// <summary>
    ///     An optional human-readable note describing the event.
    /// </summary>
    string? Note { get; set; }

    /// <summary>
    ///     The numeric identifier of the principal who triggered the event.
    /// </summary>
    Guid? UpdatedById { get; set; }

    /// <summary>
    ///     The display name of the principal who triggered the event.
    /// </summary>
    string? UpdatedBy { get; set; }
}
