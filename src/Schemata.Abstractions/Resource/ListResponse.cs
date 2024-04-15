using System.Collections.Generic;

namespace Schemata.Abstractions.Resource;

public class ListResponse<TSummary>
{
    public virtual IEnumerable<TSummary>? Entities { get; set; }

    public virtual long? TotalSize { get; set; }

    public virtual string? NextPageToken { get; set; }
}
