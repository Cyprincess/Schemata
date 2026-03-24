namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity has a discrete state, typically representing a workflow or lifecycle stage.
/// </summary>
public interface IStateful
{
    /// <summary>
    ///     Gets or sets the current state of the entity.
    /// </summary>
    string? State { get; set; }
}
