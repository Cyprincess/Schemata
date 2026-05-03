using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Standard request for deleting a resource per
///     <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso>,
///     supporting optimistic concurrency via <see cref="Etag" /> and hard-delete
///     via <see cref="Force" />.
/// </summary>
public class DeleteRequest : ICanonicalName
{
    /// <summary>
    ///     Entity tag for conditional deletion (If-Match). <see langword="null" />
    ///     means no concurrency check.
    /// </summary>
    public string? Etag { get; set; }

    /// <summary>
    ///     When <see langword="true" />, bypasses soft-delete and removes permanently.
    /// </summary>
    public bool Force { get; set; }

    #region ICanonicalName Members

    /// <inheritdoc />
    public string? Name { get; set; }

    /// <inheritdoc />
    public string? CanonicalName { get; set; }

    #endregion
}
