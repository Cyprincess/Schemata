using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation;

public class ResourceRequestContext<TRequest>
{
    public Operations Operation { get; set; }

    public TRequest? Request { get; set; }
}
