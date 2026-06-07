using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;

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
            [nameof(ResourceController<ICanonicalName, ICanonicalName, ICanonicalName, ICanonicalName>.ListAsync)]   = Operations.List,
            [nameof(ResourceController<ICanonicalName, ICanonicalName, ICanonicalName, ICanonicalName>.GetAsync)]    = Operations.Get,
            [nameof(ResourceController<ICanonicalName, ICanonicalName, ICanonicalName, ICanonicalName>.CreateAsync)] = Operations.Create,
            [nameof(ResourceController<ICanonicalName, ICanonicalName, ICanonicalName, ICanonicalName>.UpdateAsync)] = Operations.Update,
            [nameof(ResourceController<ICanonicalName, ICanonicalName, ICanonicalName, ICanonicalName>.DeleteAsync)] = Operations.Delete,
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

        controller.ControllerName            = descriptor.Plural;
        controller.RouteValues["Controller"] = descriptor.Plural;

        var collectionPath = descriptor.CollectionPath;
        var route = descriptor.Package is not null
            ? $"~/v1/{descriptor.Package.ToLowerInvariant()}/{collectionPath}"
            : $"~/v1/{collectionPath}";

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

        var quota = entityType.GetCustomAttribute<RateLimitPolicyAttribute>();
        if (quota is not null) {
            foreach (var selector in controller.Selectors) {
                selector.EndpointMetadata.Add(new EnableRateLimitingAttribute(quota.PolicyName));
            }
        }

        // Apply a scheme-specific authorization policy when a non-default
        // authentication scheme is configured for resource endpoints.
        // The always-pass assertion is intentional - we only need to set the
        // scheme, not evaluate claims; actual authorization happens in the
        // advisor pipeline.
        if (!string.IsNullOrWhiteSpace(scheme)) {
            var policy = new AuthorizationPolicyBuilder(scheme).RequireAssertion(_ => true).Build();
            controller.Filters.Add(new AuthorizeFilter(policy));
        }
    }

    #endregion
}
