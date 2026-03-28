using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Http;

/// <summary>
///     MVC convention that configures route templates, rate limiting, and optional authentication scheme
///     for generic resource controllers.
/// </summary>
public sealed class ResourceControllerConvention(string? scheme = null) : IControllerModelConvention
{
    #region IControllerModelConvention Members

    /// <inheritdoc />
    public void Apply(ControllerModel controller) {
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
            ? $"~/{descriptor.Package.ToLowerInvariant()}/{collectionPath}"
            : $"~/{collectionPath}";

        foreach (var selector in controller.Selectors) {
            selector.AttributeRouteModel?.Template = route;
        }

        var quota = entityType.GetCustomAttribute<RateLimitPolicyAttribute>();
        if (quota is not null) {
            foreach (var selector in controller.Selectors) {
                selector.EndpointMetadata.Add(new EnableRateLimitingAttribute(quota.PolicyName));
            }
        }

        if (!string.IsNullOrWhiteSpace(scheme)) {
            var policy = new AuthorizationPolicyBuilder(scheme).RequireAssertion(_ => true).Build();
            controller.Filters.Add(new AuthorizeFilter(policy));
        }
    }

    #endregion
}
