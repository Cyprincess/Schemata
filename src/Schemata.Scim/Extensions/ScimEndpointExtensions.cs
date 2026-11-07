using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Scim;
using Schemata.Scim.Controllers;
using Schemata.Scim.Filters;
using Schemata.Scim.Models;
using Schemata.Scim.Services;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the shared SCIM protocol services and controllers.</summary>
public static class ScimEndpointExtensions
{
    public static IServiceCollection AddSchemataScim(this IServiceCollection services) {
        services.AddHttpContextAccessor();
        services.TryAddScoped<ScimSchemaRegistry>();
        services.TryAddScoped<IScimBindingStore, ScimBindingStore>();
        services.AddOptions<ScimServiceProviderOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<RouteOptions>, ScimConstraintSetup>());
        services.PostConfigure<ApiBehaviorOptions>(ConfigureInvalidModelState);
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, ScimUnsupportedMediaTypeSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, ScimRouteSetup>());
        services.AddSchemataApplicationPart<ScimDiscoveryController>();
        services.AddSchemataApplicationPart<ScimResourceController>();

        return services;
    }

    private static void ConfigureInvalidModelState(ApiBehaviorOptions options) {
        var fallback = options.InvalidModelStateResponseFactory;
        options.InvalidModelStateResponseFactory = context => {
            var settings = context.HttpContext.RequestServices.GetRequiredService<IOptions<ScimServiceProviderOptions>>().Value;
            if (!ScimResults.IsScimPath(context.HttpContext.Request.Path, settings.BasePath)) {
                return fallback(context);
            }

            var messages = new List<string>();
            foreach (var entry in context.ModelState.Values) {
                foreach (var error in entry.Errors) {
                    if (!string.IsNullOrWhiteSpace(error.ErrorMessage)) {
                        messages.Add(error.ErrorMessage);
                    }
                }
            }

            var detail = messages.Count > 0 ? string.Join("; ", messages) : "The request body is not valid SCIM.";

            return ScimResults.Error(StatusCodes.Status400BadRequest, ScimConstants.InvalidSyntax, detail);
        };
    }
}

internal sealed class ScimConstraintSetup : IConfigureOptions<RouteOptions>
{
    public void Configure(RouteOptions options) {
        options.ConstraintMap.Add("scimResource", typeof(ScimResourceRouteConstraint));
    }
}

internal sealed class ScimRouteSetup(IOptions<ScimServiceProviderOptions> settings) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options) {
        options.Conventions.Add(new ScimRouteConvention(options: settings.Value));
    }

    private sealed class ScimRouteConvention(ScimServiceProviderOptions options) : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application) {
            foreach (var controller in application.Controllers) {
                if (controller.ControllerType != typeof(ScimDiscoveryController)
                    && controller.ControllerType != typeof(ScimResourceController)) {
                    continue;
                }

                foreach (var selector in controller.Selectors) {
                    Rewrite(selector);
                }

                foreach (var action in controller.Actions) {
                    foreach (var selector in action.Selectors) {
                        Rewrite(selector);
                    }
                }
            }
        }

        private void Rewrite(SelectorModel selector) {
            if (selector.AttributeRouteModel?.Template is { } template) {
                selector.AttributeRouteModel.Template = template.Replace(
                    "~" + ScimConstants.BasePath, "~" + options.BasePath, StringComparison.Ordinal);
            }
        }
    }
}
