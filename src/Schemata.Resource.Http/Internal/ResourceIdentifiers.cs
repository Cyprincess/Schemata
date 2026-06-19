using Microsoft.AspNetCore.Routing;
using Schemata.Common;

namespace Schemata.Resource.Http.Internal;

/// <summary>
///     Builds canonical resource names from HTTP route values.
/// </summary>
public static class ResourceIdentifiers
{
    /// <summary>
    ///     Builds a full resource name using the resolved parent route and relative name segment.
    /// </summary>
    /// <param name="descriptor">The resolved resource name descriptor.</param>
    /// <param name="routeValues">The current route values.</param>
    /// <param name="name">The resource-relative name segment.</param>
    /// <returns>The full resource name.</returns>
    public static string BuildFullName(ResourceNameDescriptor descriptor, RouteValueDictionary routeValues, string name) {
        var parent = descriptor.ResolveParent(routeValues);
        return parent is not null
            ? $"{parent}/{descriptor.Collection}/{name}"
            : $"{descriptor.Collection}/{name}";
    }
}
