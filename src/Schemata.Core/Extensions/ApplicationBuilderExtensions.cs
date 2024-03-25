// ReSharper disable CheckNamespace

using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

namespace Microsoft.AspNetCore.Builder;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseSchemata(
        this IApplicationBuilder app,
        IConfiguration           configuration,
        IWebHostEnvironment      environment) {
        var sp = app.ApplicationServices;

        var options = sp.GetRequiredService<SchemataOptions>();

        UseFeatures(app, configuration, environment, options);

        return app;
    }

    private static void UseFeatures(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment,
        SchemataOptions     options) {
        var modules = options.GetFeatures();
        if (modules is null) {
            return;
        }

        var features = modules.Values.ToList();

        features.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var feature in features) {
            feature.ConfigureApplication(app, configuration, environment);
        }
    }
}
