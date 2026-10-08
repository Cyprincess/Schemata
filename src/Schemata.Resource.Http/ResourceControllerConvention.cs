using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core.Building;
using Schemata.Resource.Http.Runtime;

namespace Schemata.Resource.Http;

/// <summary>
///     MVC convention that configures route templates, rate limiting, and optional authentication
///     for generic <see cref="ResourceController{TEntity,TRequest,TDetail,TSummary}" /> instances
///     per <seealso href="https://google.aip.dev/127">AIP-127: HTTP and gRPC Transcoding</seealso>.
///     Also drops controller actions for verbs that the entity's
///     <see cref="ResourceRegistration.Operations" /> whitelist excludes.
/// </summary>
public sealed class ResourceControllerConvention(
    ResourceRegistry registry,
    string?           scheme = null
) : IControllerModelConvention
{

    #region IControllerModelConvention Members

    public void Apply(ControllerModel controller) {
        // Only rewrite routes for the generic resource controller - non-generic
        // controllers are regular user-defined controllers that should be left alone.
        if (!controller.ControllerType.IsGenericType
         || controller.ControllerType.GetGenericTypeDefinition() != typeof(ResourceController<,,,>)) {
            return;
        }

        var entityType = controller.ControllerType.GetGenericArguments()[0];
        var descriptor = ResourceNameDescriptor.ForType(entityType);

        ResourceHttpConventionHelper.ApplyControllerIdentity(controller, descriptor);

        var route = ResourceHttpConventionHelper.BuildControllerRoute(descriptor);

        foreach (var selector in controller.Selectors) {
            selector.AttributeRouteModel?.Template = route;
        }

        var resource = registry.GetResource(entityType);

        if (resource is { Operations: { } allowed }) {
            var allowedSet = new HashSet<Operations>(allowed);
            for (var i = controller.Actions.Count - 1; i >= 0; i--) {
                // Same registered-identity fact the anonymous projection uses: a standard CLR
                // method name resolves to its operation; anything else is not a standard
                // operation and the whitelist never drops it.
                if (ResourceHttpConventionHelper.TryResolveOperation(controller.Actions[i], out var operation)
                 && Enum.TryParse<Operations>(operation, out var verb)
                 && !allowedSet.Contains(verb)) {
                    controller.Actions.RemoveAt(i);
                }
            }
        }

        ResourceHttpConventionHelper.ApplyRateLimit(controller, entityType);
        ResourceHttpConventionHelper.ApplyAuthorization(controller, entityType, resource?.AuthenticationScheme ?? scheme);
    }

    #endregion
}
