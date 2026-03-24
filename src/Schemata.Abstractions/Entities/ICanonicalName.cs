namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity has a human-readable name and a canonical (fully-qualified) resource name.
/// </summary>
public interface ICanonicalName
{
    /// <summary>
    ///     Gets or sets the short name of the resource.
    /// </summary>
    string? Name { get; set; }

    /// <summary>
    ///     Gets or sets the fully-qualified canonical name (e.g., "publishers/acme/books/les-miserables").
    /// </summary>
    string? CanonicalName { get; set; }
}
