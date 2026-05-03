using System.Collections.Generic;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result container for a list operation per
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>,
///     carrying matched items, total count, and a continuation token.
/// </summary>
/// <typeparam name="TSummary">The type of each item in the list.</typeparam>
public class ListResult<TSummary> : OperationResult<ListResult<TSummary>>
{
    /// <summary>
    ///     The matched resource summaries for the current page.
    /// </summary>
    public virtual IEnumerable<TSummary>? Entities { get; set; }

    /// <summary>
    ///     Total number of matching resources across all pages.
    ///     May be <see langword="null" /> when the server cannot compute a total.
    /// </summary>
    public virtual int? TotalSize { get; set; }

    /// <summary>
    ///     Token for retrieving the next page. <see langword="null" /> or empty
    ///     signals the last page.
    /// </summary>
    public virtual string? NextPageToken { get; set; }

    /// <inheritdoc />
    protected override bool IsValid() { return Entities != null; }
}
