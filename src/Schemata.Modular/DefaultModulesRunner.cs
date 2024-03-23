using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Modular;
using Schemata.Core;

namespace Schemata.Modular;

public class DefaultModulesRunner : IModulesRunner
{
    private readonly SchemataOptions _options;

    private DefaultModulesRunner(SchemataOptions options) {
        _options = options;
    }

    #region IModulesRunner Members

    public void ConfigureServices(
        IServiceCollection  services,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var modules = _options.GetModules();
        if (modules is not { Count: > 0 }) {
            return;
        }

        var startups = modules.Select(m => (IModule)Activator.CreateInstance(m)!).ToList();

        startups.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var startup in startups) {
            services.TryAddSingleton(startup.GetType(), _ => startup);
            services.TryAddEnumerableSingleton<IModule>(_ => startup);

            if (startup.GetType().GetMethod(nameof(ConfigureServices)) is null) {
                continue;
            }

            InvokerUtilities.CallMethod(startup, nameof(ConfigureServices), services, configuration, environment);
        }
    }

    public void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var modules = _options.GetModules();
        if (modules is not { Count: > 0 }) {
            return;
        }

        var sp = app.ApplicationServices;

        var startups = modules.Select(m => (IModule)Activator.CreateInstance(m)!).ToList();

        startups.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var startup in startups) {
            if (startup.GetType().GetMethod(nameof(ConfigureApplication)) is null) {
                continue;
            }

            InvokerUtilities.CallMethod(sp, startup, nameof(ConfigureApplication), app, configuration, environment);
        }
    }

    public void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment) {
        var modules = _options.GetModules();
        if (modules is not { Count: > 0 }) {
            return;
        }

        var sp = app.ApplicationServices;

        var startups = modules.Select(m => (IModule)Activator.CreateInstance(m)!).ToList();

        startups.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var startup in startups) {
            if (startup.GetType().GetMethod(nameof(ConfigureEndpoints)) is null) {
                continue;
            }

            InvokerUtilities.CallMethod(sp, startup, nameof(ConfigureEndpoints), app, endpoints, configuration, environment);
        }
    }

    #endregion

    public static DefaultModulesRunner Create(
        SchemataOptions     options,
        IConfiguration      configuration,
        IWebHostEnvironment environment,
        IServiceCollection  services) {
        var runner = new DefaultModulesRunner(options);
        runner.ConfigureServices(services, configuration, environment);
        return runner;
    }
}
