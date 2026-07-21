using System.Linq;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Internal;

internal static class HttpResourceHelper
{
    internal static bool IsHttpEnabled(ResourceAttribute resource) {
        return resource.Endpoints is null
            || resource.Endpoints.Count == 0
            || resource.Endpoints.Any(e => e == HttpResourceAttribute.Name);
    }
}
