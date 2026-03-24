namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity has a unique numeric identifier.
/// </summary>
public interface IIdentifier
{
    /// <summary>
    ///     Gets or sets the unique identifier for the entity.
    /// </summary>
    long Id { get; set; }
}
