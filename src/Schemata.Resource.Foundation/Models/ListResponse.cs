using System.Collections.Generic;
using System.Text.Json.Serialization;
using Schemata.Resource.Foundation.Converters;

namespace Schemata.Resource.Foundation.Models;

public class ListResponse<TSummary>
{
    [JsonConverter(typeof(ListResponseJsonConverter))]
    public virtual IEnumerable<TSummary>? Entities { get; set; }

    public virtual long? TotalSize { get; set; }

    public virtual int? PageSize { get; set; }

    public virtual int? Skip { get; set; }

    public virtual string? NextPageToken { get; set; }
}
