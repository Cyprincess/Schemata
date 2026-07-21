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
    ///     Gets or sets the registered resources keyed by entity type handle.
    ///     Used to filter to HTTP-enabled resources only.
    /// </summary>
    public Dictionary<RuntimeTypeHandle, ResourceAttribute> Resources { get; set; } = [];

    /// <summary>
    ///     Gets or sets the AIP-136 custom methods declared by each resource.
    /// </summary>
    public Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> Methods { get; set; } = [];

    #region IApplicationFeatureProvider<ControllerFeature> Members

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        foreach (var (handle, methods) in Methods) {
            if (!Resources.TryGetValue(handle, out var resource)) {
                continue;
            }

            if (!HttpResourceHelper.IsHttpEnabled(resource)) {
                continue;
            }

            foreach (var method in methods) {
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
