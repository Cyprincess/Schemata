namespace Schemata.Abstractions.Entities;

/// <summary>
///     Records the principal that owns an entity for authorization decisions.
/// </summary>
public interface IOwnable
{
    /// <summary>
    ///     The canonical name of the principal that owns this resource (e.g., <c>"users/chino"</c>).
    /// </summary>
    string? Owner { get; set; }
}
