// ReSharper disable CheckNamespace

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Modular;

namespace Microsoft.AspNetCore.Builder;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseModularControllers(this IApplicationBuilder app) {
        var sp = app.ApplicationServices;

        var options = sp.GetRequiredService<SchemataOptions>();

        if (!options.HasFeature<SchemataControllersFeature>()) {
            return app;
        }

        var modules = options.GetModules();
        if (modules is not { Count: > 0 }) {
            return app;
        }

        var part = sp.GetRequiredService<ModularApplicationPart>();

        foreach (var module in modules) {
            part.AddAssembly(module.Assembly);
        }

        part.Commit();

        return app;
    }

    public static IApplicationBuilder UseModular(
        this IApplicationBuilder app,
        IModulesRunner           runner,
        IConfiguration           configuration,
        IWebHostEnvironment      environment) {
        runner.ConfigureApplication(app, configuration, environment);

        return app;
    }
}
