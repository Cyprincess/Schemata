using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Schemata.Core.Building;
using Schemata.Resource.Foundation;
using Schemata.Resource.Http.Runtime;

namespace Schemata.Resource.Http;

/// <summary>
///     Synthesizes one closed
///     <see cref="ResourceMethodController{TEntity, TRequest, TResponse}" /> per distinct custom
///     method request/response shape and adds it to MVC controller discovery.
/// </summary>
public sealed class ResourceMethodControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    /// <summary>
    ///     Gets or sets the registry supplying the resources and their AIP-136 custom methods.
    /// </summary>
    public ResourceRegistry? Registry { get; set; }

    #region IApplicationFeatureProvider<ControllerFeature> Members

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        foreach (var resource in Registry?.Resources ?? []) {
            if (!HttpResourceHelper.IsHttpEnabled(resource)) {
                continue;
            }

            foreach (var method in Registry!.GetMethods(resource.Entity)) {
                var descriptor = ResourceMethodHandlerHelper.Describe(resource.Entity, method.Handler);
                if (descriptor is null) {
                    continue;
                }

                var controller = typeof(ResourceMethodController<,,>)
                                .MakeGenericType(
                                     descriptor.Entity,
                                     descriptor.Request,
                                     descriptor.Response)
                                 .GetTypeInfo();

                if (feature.Controllers.Contains(controller)) {
                    continue;
                }

                feature.Controllers.Add(controller);
            }
        }
    }

    #endregion
}
