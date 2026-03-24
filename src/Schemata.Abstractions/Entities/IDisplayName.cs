namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity has user-facing display names.
/// </summary>
public interface IDisplayName
{
    /// <summary>
    ///     Gets or sets the primary display name of the entity.
    /// </summary>
    string? DisplayName { get; set; }

    /// <summary>
    ///     Gets or sets alternative or localized display names, typically serialized as JSON.
    /// </summary>
    string? DisplayNames { get; set; }
}
