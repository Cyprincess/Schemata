using System.Collections.Generic;
using System.Reflection;
using Humanizer;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;

namespace Schemata.Resource.Http;

public sealed class ResourceControllerConvention : IControllerModelConvention
{
    private readonly IOptions<SchemataResourceOptions> _options;

    public ResourceControllerConvention(IOptions<SchemataResourceOptions> options) { _options = options; }

    #region IControllerModelConvention Members

    public void Apply(ControllerModel controller) {
        if (!controller.ControllerType.IsGenericType
         || controller.ControllerType.GetGenericTypeDefinition() != typeof(ResourceController<,,,>)) {
            return;
        }

        var entityType = controller.ControllerType.GetGenericArguments()[0];

        var resource = _options.Value.Resources.GetValueOrDefault(entityType.TypeHandle);

        var entityName = resource?.Entity.Name ?? entityType.Name;
        var plural     = entityName.Pluralize();

        controller.ControllerName            = plural;
        controller.RouteValues["Controller"] = plural;

        var canonicalAttr = entityType.GetCustomAttribute<CanonicalNameAttribute>();
        var collectionPath = canonicalAttr is not null
            ? GetCollectionPath(canonicalAttr.ResourceName)
            : plural.ToLowerInvariant();

        var package = resource?.Package;
        var route   = package is not null ? $"~/{package.ToLowerInvariant()}/{collectionPath}" : $"~/{collectionPath}";

        foreach (var selector in controller.Selectors) {
            selector.AttributeRouteModel?.Template = route;
        }
    }

    #endregion

    private static string GetCollectionPath(string resourceName) {
        var lastSlash = resourceName.LastIndexOf('/');
        return lastSlash > 0 ? resourceName[..lastSlash] : resourceName;
    }
}
