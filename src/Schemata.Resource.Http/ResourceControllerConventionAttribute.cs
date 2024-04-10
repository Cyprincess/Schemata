using System;
using Humanizer;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Schemata.Resource.Http;

[AttributeUsage(AttributeTargets.Class)]
public class ResourceControllerConventionAttribute : Attribute, IControllerModelConvention
{
    #region IControllerModelConvention Members

    public void Apply(ControllerModel controller) {
        if (!controller.ControllerType.IsGenericType
         || controller.ControllerType.GetGenericTypeDefinition() != typeof(ResourceController<,,,>)) {
            return;
        }

        var entity = controller.ControllerType.GetGenericArguments()[0];

        var resource = entity.Name.Pluralize();

        controller.ControllerName            = resource;
        controller.RouteValues["Controller"] = resource;
    }

    #endregion
}
