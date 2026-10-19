using System.Linq;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;

namespace Schemata.Resource.Http.Runtime;

internal static class HttpResourceHelper
{
    internal static bool IsHttpEnabled(ResourceRegistration resource) {
        return resource.Endpoints is null
            || resource.Endpoints.Count == 0
            || resource.Endpoints.Any(e => e == HttpResourceAttribute.Name);
    }
}
