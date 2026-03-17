using System.Collections.Generic;

namespace Schemata.Abstractions.Resource;

public class ListResult<TSummary> : OperationResult<ListResult<TSummary>>
{
    public virtual IEnumerable<TSummary>? Entities { get; set; }

    public virtual int? TotalSize { get; set; }

    public virtual string? NextPageToken { get; set; }

    protected override bool IsValid() { return Entities != null; }
}
