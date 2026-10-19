namespace Schemata.Abstractions.Resource;

/// <summary>
///     Standard list-request parameters for
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>
///     with <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>,
///     ordering, and <seealso href="https://google.aip.dev/158">AIP-158: Pagination</seealso>.
/// </summary>
public class ListRequest
{
    /// <summary>
    ///     The parent resource name; narrows the list to children of the given parent.
    ///     When <see langword="null" />, lists top-level resources.
    /// </summary>
    public virtual string? Parent { get; set; }

    /// <summary>
    ///     A filter expression conforming to <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>.
    /// </summary>
    public virtual string? Filter { get; set; }

    /// <summary>
    ///     The filter expression language; defaults to the resource's first enabled language.
    /// </summary>
    public virtual string? Language { get; set; }

    /// <summary>
    ///     Comma-separated field paths with optional <c>asc</c>/<c>desc</c> suffix
    ///     (e.g., <c>"create_time desc,name asc"</c>).
    /// </summary>
    public virtual string? OrderBy { get; set; }

    /// <summary>
    ///     When <see langword="true" />, includes soft-deleted resources.
    /// </summary>
    public virtual bool? ShowDeleted { get; set; }

    /// <summary>
    ///     Maximum number of items per page. On an initial request, omitted or zero adopts the
    ///     effective default (25 unless configured otherwise); on a continuation, omitted keeps
    ///     the previous page's effective size. Values above the effective maximum coerce to it;
    ///     negative values are rejected as invalid arguments. The effective policy comes from
    ///     the global resource options and optional per-resource overrides.
    /// </summary>
    public virtual int? PageSize { get; set; }

    /// <summary>
    ///     Number of items to skip before the first result. Used for offset pagination.
    /// </summary>
    public virtual int? Skip { get; set; }

    /// <summary>
    ///     An opaque token returned from a previous <see cref="ListResultBase{TEntity,TSummary}.NextPageToken" />
    ///     to continue pagination.
    /// </summary>
    public virtual string? PageToken { get; set; }
}
