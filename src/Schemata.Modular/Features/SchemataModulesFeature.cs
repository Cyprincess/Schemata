using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Modular.Features;

public sealed class SchemataModulesFeature<TProvider, TRunner> : FeatureBase where TProvider : class, IModulesProvider
                                                                             where TRunner : class, IModulesRunner
{
    public override int Priority => 2_147_200_000;

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
