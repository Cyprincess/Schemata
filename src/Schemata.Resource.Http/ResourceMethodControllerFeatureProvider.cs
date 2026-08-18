using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Resource.Http.Internal;

namespace Schemata.Resource.Http;

/// <summary>
///     Synthesizes a closed-generic
///     <see cref="ResourceMethodController{TEntity, TRequest, TResponse, THandler}" />
///     per AIP-136 custom method declared via
///     <see cref="ResourceMethodAttribute" /> on each registered resource, and
///     adds them to the MVC controller feature so they participate in routing.
/// </summary>
public sealed class ResourceMethodControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    /// <summary>
    ///     Gets or sets the registry supplying the resources and their AIP-136 custom methods.
    /// </summary>
    public IResourceRegistry? Registry { get; set; }

    #region IApplicationFeatureProvider<ControllerFeature> Members

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        foreach (var resource in Registry?.Resources ?? []) {
            if (!HttpResourceHelper.IsHttpEnabled(resource)) {
                continue;
            }

            foreach (var method in Registry!.GetMethods(resource.Entity)) {
                var handlerInterface = ResourceMethodHandlerHelper.FindHandlerInterface(method.Handler);
                if (handlerInterface is null) {
                    continue;
                }

                var arguments = handlerInterface.GetGenericArguments();
                var entity    = arguments[0];
                var request   = arguments[1];
                var response  = arguments[2];

                var controller = typeof(ResourceMethodController<,,,>)
                                .MakeGenericType(entity, request, response, method.Handler)
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
