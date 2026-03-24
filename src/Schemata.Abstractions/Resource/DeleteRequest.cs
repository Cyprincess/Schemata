using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Standard request parameters for deleting a resource.
/// </summary>
public class DeleteRequest : ICanonicalName
{
    /// <summary>
    ///     Gets or sets the entity tag for conditional deletion (optimistic concurrency).
    /// </summary>
    public string? Etag { get; set; }

    /// <summary>
    ///     Gets or sets whether to force-delete the resource, bypassing soft-delete.
    /// </summary>
    public bool Force { get; set; }

    #region ICanonicalName Members

    /// <inheritdoc />
    public string? Name { get; set; }

    /// <inheritdoc />
    public string? CanonicalName { get; set; }

    #endregion
}
