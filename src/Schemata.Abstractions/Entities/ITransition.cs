namespace Schemata.Abstractions.Entities;

/// <summary>Records an audit event capturing who performed what action.</summary>
public interface ITransition
{
    /// <summary>The event type identifier (e.g. <c>created</c>, <c>updated</c>, <c>deleted</c>).</summary>
    string Event { get; set; }

    /// <summary>An optional human-readable note describing the event.</summary>
    string? Note { get; set; }

    /// <summary>The canonical resource name of the principal who triggered the event.</summary>
    string? UpdatedBy { get; set; }
}
