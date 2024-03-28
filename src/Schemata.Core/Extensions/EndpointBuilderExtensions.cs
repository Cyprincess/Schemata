using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class EndpointBuilderExtensions
{
    public static IEndpointRouteBuilder UseSchemata(
        this IEndpointRouteBuilder endpoints,
        IApplicationBuilder        app,
        IConfiguration             configuration,
        IWebHostEnvironment        environment) {
        var sp = app.ApplicationServices;

        var options = sp.GetRequiredService<SchemataOptions>();

        UseFeatures(endpoints, configuration, environment, options);

        return endpoints;
    }

    private static void UseFeatures(
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment,
        SchemataOptions       options) {
        var modules = options.GetFeatures();
        if (modules is null) {
            return;
        }

        var features = modules.Values.ToList();

        features.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var feature in features) {
            feature.ConfigureEndpoints(endpoints, configuration, environment);
        }
    }
}
