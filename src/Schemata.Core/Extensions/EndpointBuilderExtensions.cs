using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for configuring Schemata endpoints on <see cref="IEndpointRouteBuilder" />.
/// </summary>
public static class EndpointBuilderExtensions
{
    /// <summary>
    ///     Configures endpoints for all registered Schemata features.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder UseSchemata(
        this IEndpointRouteBuilder endpoints,
        IApplicationBuilder        app,
        IConfiguration             configuration,
        IWebHostEnvironment        environment
    ) {
        var sp = app.ApplicationServices;

        var schemata = sp.GetRequiredService<SchemataOptions>();

        var modules = schemata.GetFeatures();
        if (modules is null) {
            return endpoints;
        }

        var features = modules.Values.ToList();

        features.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var feature in features) {
            feature.ConfigureEndpoints(app, endpoints, configuration, environment);
        }

        return endpoints;
    }
}
