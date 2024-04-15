using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Abstractions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Modular.Features;

public sealed class SchemataModulesFeature<TProvider, TRunner> : FeatureBase
    where TProvider : class, IModulesProvider
    where TRunner : class, IModulesRunner
{
    public override int Priority => Constants.Orders.Max;

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

        foreach (var module in modules) {
            var resources = module.Assembly.DefinedTypes
                                  .SelectMany(t => t.GetCustomAttributes<ResourceAttribute>())
                                  .ToList();

            if (resources.Count == 0) {
                continue;
            }

            services.Configure<SchemataResourceOptions>(options => {
                foreach (var resource in resources) {
                    var authorize = resource.Entity.GetCustomAttribute<AuthorizeAttribute>();
                    if (authorize is not null) {
                        var policy = new ResourcePolicyAttribute {
                            Methods = string.Join(",", [
                                nameof(resource.List), nameof(resource.Get), nameof(resource.Create),
                                nameof(resource.Update), nameof(resource.Delete),
                            ]),
                            Policy                = authorize.Policy,
                            Roles                 = authorize.Roles,
                            AuthenticationSchemes = authorize.AuthenticationSchemes,
                        };
                        resource.List   ??= policy;
                        resource.Get    ??= policy;
                        resource.Create ??= policy;
                        resource.Update ??= policy;
                        resource.Delete ??= policy;
                    }

                    options.Resources[resource.Entity] = resource;
                }
            });
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
