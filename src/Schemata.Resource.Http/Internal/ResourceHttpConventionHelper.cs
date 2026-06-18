using System;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Http.Internal;

internal static class ResourceHttpConventionHelper
{
    public static string BuildControllerRoute(ResourceNameDescriptor descriptor) {
        var collectionPath = descriptor.CollectionPath;
        return descriptor.Package is not null
            ? $"~/v1/{descriptor.Package.ToLowerInvariant()}/{collectionPath}"
            : $"~/v1/{collectionPath}";
    }

    public static void ApplyControllerIdentity(ControllerModel controller, ResourceNameDescriptor descriptor) {
        controller.ControllerName            = descriptor.Plural;
        controller.RouteValues["Controller"] = descriptor.Plural;
    }

    public static void ApplyRateLimit(ControllerModel controller, Type entityType) {
        var quota = entityType.GetCustomAttribute<RateLimitPolicyAttribute>();
        if (quota is null) {
            return;
        }

        foreach (var selector in controller.Selectors) {
            selector.EndpointMetadata.Add(new EnableRateLimitingAttribute(quota.PolicyName));
        }
    }

    public static void ApplyAuthorization(ControllerModel controller, string? scheme) {
        if (string.IsNullOrWhiteSpace(scheme)) {
            return;
        }

        var policy = new AuthorizationPolicyBuilder(scheme).RequireAssertion(_ => true).Build();
        controller.Filters.Add(new AuthorizeFilter(policy));
    }
}
