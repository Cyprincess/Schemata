namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity represents an audit event or state change log entry.
/// </summary>
public interface IEvent
{
    /// <summary>
    ///     Gets or sets the event type identifier.
    /// </summary>
    string Event { get; set; }

    /// <summary>
    ///     Gets or sets an optional note or description for the event.
    /// </summary>
    string? Note { get; set; }

    /// <summary>
    ///     Gets or sets the numeric identifier of the user who triggered the event.
    /// </summary>
    long? UpdatedById { get; set; }

    /// <summary>
    ///     Gets or sets the display name of the user who triggered the event.
    /// </summary>
    string? UpdatedBy { get; set; }
}
