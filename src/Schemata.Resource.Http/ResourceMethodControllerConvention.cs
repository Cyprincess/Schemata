using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Http.Internal;

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

        var handlerMethods = resourceMethods.Where(m => m.Handler == handler).ToList();
        if (handlerMethods.Count == 0) {
            return;
        }

        var descriptor = ResourceNameDescriptor.ForType(entity);

        ResourceHttpConventionHelper.ApplyControllerIdentity(controller, descriptor);

        var controllerRoute = ResourceHttpConventionHelper.BuildControllerRoute(descriptor);

        // Suppress the controller-level template. AIP-136 requires `{collection}:verb` with
        // no `/` separator before the colon, but MVC's AttributeRouteModel.CombineTemplates
        // unconditionally inserts a `/` between the controller and action templates. Setting
        // each action's selector to an absolute (`~/...`) template lets it stand alone.
        foreach (var selector in controller.Selectors) {
            selector.AttributeRouteModel = null;
        }

        // One handler may be registered for several verbs, but the generated controller is a single
        // closed type. Clone its action once per verb and bind each clone to its own `{...}:verb`
        // route, so every verb gets a distinct endpoint instead of collapsing onto the first.
        var pristine = controller.Actions.ToList();
        controller.Actions.Clear();
        foreach (var method in handlerMethods) {
            foreach (var template in pristine) {
                var action = new ActionModel(template) { Controller = controller };
                ConfigureMethodAction(action, method, controllerRoute);
                controller.Actions.Add(action);
            }
        }

        ResourceHttpConventionHelper.ApplyRateLimit(controller, entity);
        ResourceHttpConventionHelper.ApplyAuthorization(controller, scheme);
    }

    private static void ConfigureMethodAction(ActionModel action, ResourceMethodAttribute method, string controllerRoute) {
        action.ActionName = $"Invoke_{method.Verb}";

        var actionTemplate = method.Scope == ResourceMethodScope.Instance
            ? $"{controllerRoute}/{{name}}:{method.Verb}"
            : $"{controllerRoute}:{method.Verb}";

        foreach (var selector in action.Selectors) {
            selector.AttributeRouteModel = new() {
                Template = actionTemplate,
            };

            // Carry the verb to runtime so the shared controller dispatches the matched endpoint
            // to the correct custom method.
            selector.EndpointMetadata.Add(new ResourceMethodVerbMetadata(method.Verb));
        }

        if (method.Method == ResourceHttpMethod.Get) {
            ApplyGetBinding(action);
        }
    }

    #endregion

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
