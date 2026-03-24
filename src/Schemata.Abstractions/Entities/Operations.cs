namespace Schemata.Abstractions.Entities;

/// <summary>
///     Standard CRUD operations for resource APIs.
/// </summary>
public enum Operations
{
    /// <summary>
    ///     List resources matching a filter.
    /// </summary>
    List,

    /// <summary>
    ///     Retrieve a single resource by name.
    /// </summary>
    Get,

    /// <summary>
    ///     Create a new resource.
    /// </summary>
    Create,

    /// <summary>
    ///     Update an existing resource.
    /// </summary>
    Update,

    /// <summary>
    ///     Delete a resource.
    /// </summary>
    Delete,
}
