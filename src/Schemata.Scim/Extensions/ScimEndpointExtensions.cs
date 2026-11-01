using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Scim;
using Schemata.Scim.Controllers;
using Schemata.Scim.Filters;
using Schemata.Scim.Models;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registers the shared SCIM protocol services and discovery endpoints.
/// </summary>
public static class ScimEndpointExtensions
{
    /// <summary>
    ///     Registers the schema registry, service provider options, model-state and media-type error
    ///     shapes, and the discovery controller.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataScim(this IServiceCollection services) {
        services.TryAddSingleton<ScimSchemaRegistry>();
        services.AddOptions<ScimServiceProviderOptions>();
        services.PostConfigure<ApiBehaviorOptions>(ConfigureInvalidModelState);
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, ScimUnsupportedMediaTypeSetup>());
        services.AddSchemataApplicationPart<ScimDiscoveryController>();

        return services;
    }

    /// <summary>
    ///     Registers a resource type and the schemas it references with discovery.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="resourceType">The resource type.</param>
    /// <param name="schemas">The schemas referenced by the resource type.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataScimResourceType(
        this IServiceCollection services,
        ScimResourceType         resourceType,
        params ScimSchema[]      schemas
    ) {
        services.AddSchemataScim();
        services.AddSingleton(resourceType);
        foreach (var schema in schemas) {
            services.AddSingleton(schema);
        }

        return services;
    }

    private static void ConfigureInvalidModelState(ApiBehaviorOptions options) {
        var fallback = options.InvalidModelStateResponseFactory;
        options.InvalidModelStateResponseFactory = context => {
            if (!ScimResults.IsScimPath(context.HttpContext.Request.Path)) {
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
