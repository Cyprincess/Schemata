using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Standard request for deleting a resource per
///     <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso>,
///     supporting optimistic concurrency via <see cref="Etag" />.
/// </summary>
public class DeleteRequest : ICanonicalName, IAllowMissing
{
    /// <summary>
    ///     Entity tag for conditional deletion (If-Match). <see langword="null" />
    ///     means no concurrency check.
    /// </summary>
    public string? Etag { get; set; }

    #region IAllowMissing Members

    /// <summary>
    ///     When <see langword="true" />, deleting a resource that does not exist succeeds
    ///     instead of failing with <c>NOT_FOUND</c>, per AIP-135.
    /// </summary>
    public bool AllowMissing { get; set; }

    #endregion

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
