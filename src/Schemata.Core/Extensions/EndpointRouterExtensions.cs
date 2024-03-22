// ReSharper disable CheckNamespace

using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

namespace Microsoft.AspNetCore.Builder;

public static class EndpointRouterExtensions
{
    public static IEndpointRouteBuilder UseSchemata(
        this IEndpointRouteBuilder endpoint,
        IApplicationBuilder        app,
        IConfiguration             configuration,
        IWebHostEnvironment        environment) {
        var sp = app.ApplicationServices;

        var options = sp.GetRequiredService<SchemataOptions>();

        UseFeatures(endpoint, configuration, environment, options);

        return endpoint;
    }

    private static void UseFeatures(
        IEndpointRouteBuilder endpoint,
        IConfiguration        configuration,
        IWebHostEnvironment   environment,
        SchemataOptions       options) {
        var modules = options.GetFeatures();
        if (modules is null) {
            return;
        }

        var features = modules.ToList();

        features.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var feature in features) {
            feature.ConfigureEndpoint(endpoint, configuration, environment);
        }
    }
}
