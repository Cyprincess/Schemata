using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Http.Internal;

namespace Schemata.Resource.Http;

/// <summary>
///     MVC convention that configures route templates, rate limiting, and optional authentication
///     for generic <see cref="ResourceController{TEntity,TRequest,TDetail,TSummary}" /> instances
///     per <seealso href="https://google.aip.dev/127">AIP-127: HTTP and gRPC Transcoding</seealso>.
///     Also drops controller actions for verbs that the entity's
///     <see cref="ResourceAttribute.Operations" /> whitelist excludes.
/// </summary>
public sealed class ResourceControllerConvention(
    IReadOnlyDictionary<RuntimeTypeHandle, ResourceAttribute> resources,
    string?                                                   scheme = null
) : IControllerModelConvention
{
    // Custom methods are handled by ResourceMethodControllerConvention and are unaffected.
    private static readonly IReadOnlyDictionary<string, Operations> VerbByAction =
        new Dictionary<string, Operations>(StringComparer.Ordinal) {
            [nameof(ResourceController<,,,>.ListAsync)]   = Operations.List,
            [nameof(ResourceController<,,,>.GetAsync)]    = Operations.Get,
            [nameof(ResourceController<,,,>.CreateAsync)] = Operations.Create,
            [nameof(ResourceController<,,,>.UpdateAsync)] = Operations.Update,
            [nameof(ResourceController<,,,>.DeleteAsync)] = Operations.Delete,
        };

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

        if (resources.TryGetValue(entityType.TypeHandle, out var resource)
         && resource.Operations is { } allowed) {
            var allowedSet = new HashSet<Operations>(allowed);
            for (var i = controller.Actions.Count - 1; i >= 0; i--) {
                if (VerbByAction.TryGetValue(controller.Actions[i].ActionName, out var verb)
                 && !allowedSet.Contains(verb)) {
                    controller.Actions.RemoveAt(i);
                }
            }
        }

        ResourceHttpConventionHelper.ApplyRateLimit(controller, entityType);
        ResourceHttpConventionHelper.ApplyAuthorization(controller, scheme);
    }

    #endregion
}
