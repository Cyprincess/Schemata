using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Http;

/// <summary>
///     MVC convention that wires up AIP-136 custom-method routes on closed
///     <see cref="ResourceMethodController{TEntity, TRequest, TResponse, THandler}" />
///     instances. The convention picks the
///     <see cref="ResourceMethodAttribute" /> matching the controller's
///     <c>THandler</c> and rewrites the action template to
///     <c>{name}:{verb}</c> (Instance scope) or <c>:{verb}</c> (Collection scope).
/// </summary>
public sealed class ResourceMethodControllerConvention(
    Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> methods,
    string?                                                      scheme = null
) : IControllerModelConvention
{
    #region IControllerModelConvention Members

    public void Apply(ControllerModel controller) {
        if (!controller.ControllerType.IsGenericType
         || controller.ControllerType.GetGenericTypeDefinition() != typeof(ResourceMethodController<,,,>)) {
            return;
        }

        var genericArguments = controller.ControllerType.GetGenericArguments();
        var entity           = genericArguments[0];
        var handler          = genericArguments[3];

        if (!methods.TryGetValue(entity.TypeHandle, out var resourceMethods)) {
            return;
        }

        var method = resourceMethods.FirstOrDefault(m => m.Handler == handler);
        if (method is null) {
            return;
        }

        var descriptor = ResourceNameDescriptor.ForType(entity);

        controller.ControllerName            = descriptor.Plural;
        controller.RouteValues["Controller"] = descriptor.Plural;

        var collectionPath = descriptor.CollectionPath;
        var controllerRoute = descriptor.Package is not null
            ? $"~/v1/{descriptor.Package.ToLowerInvariant()}/{collectionPath}"
            : $"~/v1/{collectionPath}";

        // Suppress the controller-level template. AIP-136 requires `{collection}:verb` with
        // no `/` separator before the colon, but MVC's AttributeRouteModel.CombineTemplates
        // unconditionally inserts a `/` between the controller and action templates. Setting
        // each action's selector to an absolute (`~/...`) template lets it stand alone.
        foreach (var selector in controller.Selectors) {
            selector.AttributeRouteModel = null;
        }

        var actionTemplate = method.Scope == ResourceMethodScope.Instance
            ? $"{controllerRoute}/{{name}}:{method.Verb}"
            : $"{controllerRoute}:{method.Verb}";

        foreach (var action in controller.Actions) {
            action.ActionName = $"Invoke_{method.Verb}";
            foreach (var selector in action.Selectors) {
                selector.AttributeRouteModel = new() {
                    Template = actionTemplate,
                };
            }

            if (method.Method == ResourceHttpMethod.Get) {
                ApplyGetBinding(action);
            }
        }

        var quota = entity.GetCustomAttribute<RateLimitPolicyAttribute>();
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

    /// <summary>
    ///     Rebinds a read-only custom method to HTTP GET per AIP-136: replaces the
    ///     action's POST constraint and metadata, and binds the request DTO from the
    ///     query string since GET requests carry no body.
    /// </summary>
    private static void ApplyGetBinding(ActionModel action) {
        foreach (var selector in action.Selectors) {
            for (var i = selector.ActionConstraints.Count - 1; i >= 0; i--) {
                if (selector.ActionConstraints[i] is HttpMethodActionConstraint) {
                    selector.ActionConstraints.RemoveAt(i);
                }
            }

            selector.ActionConstraints.Add(new HttpMethodActionConstraint([HttpMethods.Get]));

            for (var i = selector.EndpointMetadata.Count - 1; i >= 0; i--) {
                if (selector.EndpointMetadata[i] is IHttpMethodMetadata) {
                    selector.EndpointMetadata.RemoveAt(i);
                }
            }

            selector.EndpointMetadata.Add(new HttpMethodMetadata([HttpMethods.Get]));
        }

        foreach (var parameter in action.Parameters) {
            if (parameter.ParameterName == "request") {
                parameter.BindingInfo = new() { BindingSource = BindingSource.Query };
            }
        }
    }
}
