using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.RateLimiting;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Http.Runtime;

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
    ///     Adds endpoint authorization metadata requiring the configured authentication scheme.
    ///     Actions whose operation the entity's
    ///     <see cref="AnonymousAttribute" /> exempts keep that exemption at the transport boundary,
    ///     matching the dispatcher-side authentication advisor.
    /// </summary>
    /// <param name="controller">The controller model to update.</param>
    /// <param name="entityType">The resource entity type.</param>
    /// <param name="scheme">The authentication scheme.</param>
    public static void ApplyAuthorization(ControllerModel controller, Type entityType, string? scheme) {
        if (string.IsNullOrWhiteSpace(scheme)) {
            return;
        }

        var policy = new AuthorizationPolicyBuilder(scheme).RequireAuthenticatedUser().Build();
        foreach (var selector in controller.Selectors) {
            selector.EndpointMetadata.Add(new AuthorizeAttribute { AuthenticationSchemes = scheme });
            selector.EndpointMetadata.Add(policy);
        }

        foreach (var action in controller.Actions) {
            if (TryResolveOperation(action, out var operation) && AnonymousAccess.IsAnonymous(entityType, operation)) {
                foreach (var selector in action.Selectors) selector.EndpointMetadata.Add(new AllowAnonymousAttribute());
            }
        }
    }

    private static readonly IReadOnlyDictionary<string, string> OperationByStandardMethod =
        new Dictionary<string, string>(StringComparer.Ordinal) {
            [nameof(ResourceController<,,,>.ListAsync)]   = nameof(Operations.List),
            [nameof(ResourceController<,,,>.GetAsync)]    = nameof(Operations.Get),
            [nameof(ResourceController<,,,>.CreateAsync)] = nameof(Operations.Create),
            [nameof(ResourceController<,,,>.UpdateAsync)] = nameof(Operations.Update),
            [nameof(ResourceController<,,,>.DeleteAsync)] = nameof(Operations.Delete),
        };

    /// <summary>
    ///     Resolves the registered operation identity for a resource action: a custom verb from
    ///     the <see cref="ResourceMethodVerbMetadata" /> the method convention attached at
    ///     registration, or a standard operation from the CLR <see cref="MethodInfo" /> name.
    ///     The display <see cref="ActionModel.ActionName" /> is never consulted — MVC strips the
    ///     Async suffix from it by default, so it is not a stable identity.
    /// </summary>
    /// <param name="action">The action model to resolve.</param>
    /// <param name="operation">The resolved operation or custom verb.</param>
    /// <returns>Whether a registered operation identity was found.</returns>
    internal static bool TryResolveOperation(ActionModel action, [NotNullWhen(true)] out string? operation) {
        foreach (var selector in action.Selectors) {
            foreach (var metadata in selector.EndpointMetadata) {
                if (metadata is ResourceMethodVerbMetadata verb) {
                    operation = verb.Verb;
                    return true;
                }
            }
        }

        return OperationByStandardMethod.TryGetValue(action.ActionMethod.Name, out operation!);
    }
}
