using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Schemata.Modular;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseModular(
        this IApplicationBuilder app,
        IModulesRunner           runner,
        IConfiguration           configuration,
        IWebHostEnvironment      environment) {
        runner.ConfigureApplication(app, configuration, environment);

        return app;
    }
}
