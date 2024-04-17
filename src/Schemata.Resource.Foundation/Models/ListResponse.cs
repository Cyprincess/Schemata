using System.Collections.Generic;

namespace Schemata.Resource.Foundation.Models;

public class ListResponse<TSummary>
{
    public virtual IEnumerable<TSummary>? Entities { get; set; }

    public virtual long? TotalSize { get; set; }

    public virtual string? NextPageToken { get; set; }
}
