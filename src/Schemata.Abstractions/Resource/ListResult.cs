using System.Collections.Generic;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result of a List operation containing the matched entities and pagination information.
/// </summary>
/// <typeparam name="TSummary">The type of the entity summary.</typeparam>
public class ListResult<TSummary> : OperationResult<ListResult<TSummary>>
{
    /// <summary>
    ///     Gets or sets the collection of matched entities.
    /// </summary>
    public virtual IEnumerable<TSummary>? Entities { get; set; }

    /// <summary>
    ///     Gets or sets the total count of matching entities across all pages.
    /// </summary>
    public virtual int? TotalSize { get; set; }

    /// <summary>
    ///     Gets or sets the token for retrieving the next page of results.
    /// </summary>
    public virtual string? NextPageToken { get; set; }

    /// <inheritdoc />
    protected override bool IsValid() { return Entities != null; }
}
