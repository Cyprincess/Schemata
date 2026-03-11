using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Schemata.Common;

namespace Schemata.Resource.Http;

public sealed class ResourceControllerConvention : IControllerModelConvention
{
    #region IControllerModelConvention Members

    public void Apply(ControllerModel controller) {
        if (!controller.ControllerType.IsGenericType
         || controller.ControllerType.GetGenericTypeDefinition() != typeof(ResourceController<,,,>)) {
            return;
        }

        var entityType = controller.ControllerType.GetGenericArguments()[0];
        var descriptor = ResourceNameDescriptor.ForType(entityType);

        controller.ControllerName            = descriptor.Plural;
        controller.RouteValues["Controller"] = descriptor.Plural;

        var collectionPath = descriptor.CollectionPath;
        var route = descriptor.Package is not null
            ? $"~/{descriptor.Package.ToLowerInvariant()}/{collectionPath}"
            : $"~/{collectionPath}";

        foreach (var selector in controller.Selectors) {
            selector.AttributeRouteModel?.Template = route;
        }
    }

    #endregion
}
