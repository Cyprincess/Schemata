using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Modular.Features;

public sealed class SchemataModulesFeature<TProvider, TRunner> : FeatureBase
    where TProvider : class, IModulesProvider
    where TRunner : class, IModulesRunner
{
    public override int Priority => 2_147_400_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var provider = typeof(TProvider);
        var modules = Utilities.CreateInstance<IModulesProvider>(provider, schemata.CreateLogger(provider), configuration, environment)!
                               .GetModules()
                               .ToList();
        schemata.SetModules(modules);

        if (schemata.GetFeatures()?.Any(f => f.Key.Name == "SchemataResourceFeature") == true) {
            foreach (var module in modules) {
                var resources = module.Assembly.DefinedTypes
                                      .SelectMany(t => t.GetCustomAttributes<ResourceAttribute>())
                                      .ToList();

                if (resources.Count == 0) {
                    continue;
                }

                services.Configure<SchemataResourceOptions>(options => {
                    foreach (var resource in resources) {
                        options.Resources[resource.EntityType] = resource;
                    }
                });
            }
        }

        if (services.Any(s => s.ServiceType == typeof(IModulesRunner))) {
            return;
        }

        var runner = typeof(TRunner);
        var context = Utilities.CreateInstance<IModulesRunner>(runner, schemata.CreateLogger(runner), schemata, configuration, environment)!;
        context.ConfigureServices(services, configuration, environment);
        services.TryAddSingleton<IModulesRunner>(_ => context);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var runner = app.ApplicationServices.GetRequiredService<IModulesRunner>();
        runner.ConfigureApplication(app, configuration, environment);
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment) {
        var runner = app.ApplicationServices.GetRequiredService<IModulesRunner>();
        runner.ConfigureEndpoints(app, endpoints, configuration, environment);
    }
}
