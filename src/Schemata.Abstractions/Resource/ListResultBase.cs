using System.Collections.Generic;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result container for a list operation per
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>,
///     carrying matched items, total count, and a continuation token.
/// </summary>
/// <typeparam name="TSummary">The type of each item in the list.</typeparam>
public class ListResultBase<TSummary> : IEntitiesResult<TSummary>
{
    /// <summary>
    ///     The matched resource summaries for the current page.
    /// </summary>
    public virtual IList<TSummary>? Entities { get; set; }

    /// <summary>
    ///     Total number of matching resources across all pages.
    ///     <see langword="null" /> indicates the server skipped total computation.
    /// </summary>
    public virtual int? TotalSize { get; set; }

    /// <summary>
    ///     Token for retrieving the next page. <see langword="null" /> or empty
    ///     signals the last page.
    /// </summary>
    public virtual string? NextPageToken { get; set; }
}
