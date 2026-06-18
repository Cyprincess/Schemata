using Microsoft.AspNetCore.Routing;
using Schemata.Common;

namespace Schemata.Resource.Http.Internal;

public static class ResourceIdentifiers
{
    public static string BuildFullName(ResourceNameDescriptor descriptor, RouteValueDictionary routeValues, string name) {
        var parent = descriptor.ResolveParent(routeValues);
        return parent is not null
            ? $"{parent}/{descriptor.Collection}/{name}"
            : $"{descriptor.Collection}/{name}";
    }
}
