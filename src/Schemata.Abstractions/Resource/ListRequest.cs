namespace Schemata.Abstractions.Resource;

/// <summary>
///     Standard request parameters for listing resources with filtering, ordering, and pagination.
/// </summary>
public class ListRequest
{
    /// <summary>
    ///     Gets or sets the parent resource name for scoped listings.
    /// </summary>
    public virtual string? Parent { get; set; }

    /// <summary>
    ///     Gets or sets the filter expression to apply to the listing.
    /// </summary>
    public virtual string? Filter { get; set; }

    /// <summary>
    ///     Gets or sets the order-by expression (e.g., "create_time desc").
    /// </summary>
    public virtual string? OrderBy { get; set; }

    /// <summary>
    ///     Gets or sets whether to include soft-deleted resources in the listing.
    /// </summary>
    public virtual bool? ShowDeleted { get; set; }

    /// <summary>
    ///     Gets or sets the maximum number of results to return per page.
    /// </summary>
    public virtual int? PageSize { get; set; }

    /// <summary>
    ///     Gets or sets the number of results to skip.
    /// </summary>
    public virtual int? Skip { get; set; }

    /// <summary>
    ///     Gets or sets the page token for pagination continuation.
    /// </summary>
    public virtual string? PageToken { get; set; }
}
