using System;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Http.Internal;

/// <summary>
///     Shared helpers for applying generated resource MVC conventions.
/// </summary>
internal static class ResourceHttpConventionHelper
{
    /// <summary>
    ///     Builds the absolute route template for a resource collection.
    /// </summary>
    /// <param name="descriptor">The resolved resource name descriptor.</param>
    /// <returns>The MVC route template.</returns>
    public static string BuildControllerRoute(ResourceNameDescriptor descriptor) {
        var collectionPath = descriptor.CollectionPath;
        return descriptor.Package is not null
            ? $"~/v1/{descriptor.Package.ToLowerInvariant()}/{collectionPath}"
            : $"~/v1/{collectionPath}";
    }

    /// <summary>
    ///     Applies the generated controller name and route value for a resource.
    /// </summary>
    /// <param name="controller">The controller model to update.</param>
    /// <param name="descriptor">The resolved resource name descriptor.</param>
    public static void ApplyControllerIdentity(ControllerModel controller, ResourceNameDescriptor descriptor) {
        controller.ControllerName            = descriptor.Plural;
        controller.RouteValues["Controller"] = descriptor.Plural;
    }

    /// <summary>
    ///     Adds rate-limit endpoint metadata from a resource entity attribute.
    /// </summary>
    /// <param name="controller">The controller model to update.</param>
    /// <param name="entityType">The resource entity type.</param>
    public static void ApplyRateLimit(ControllerModel controller, Type entityType) {
        var quota = entityType.GetCustomAttribute<RateLimitPolicyAttribute>();
        if (quota is null) {
            return;
        }

        foreach (var selector in controller.Selectors) {
            selector.EndpointMetadata.Add(new EnableRateLimitingAttribute(quota.PolicyName));
        }
    }

    /// <summary>
    ///     Adds an authorization filter for the configured authentication scheme.
    /// </summary>
    /// <param name="controller">The controller model to update.</param>
    /// <param name="scheme">The authentication scheme.</param>
    public static void ApplyAuthorization(ControllerModel controller, string? scheme) {
        if (string.IsNullOrWhiteSpace(scheme)) {
            return;
        }

        var policy = new AuthorizationPolicyBuilder(scheme).RequireAssertion(_ => true).Build();
        controller.Filters.Add(new AuthorizeFilter(policy));
    }
}
