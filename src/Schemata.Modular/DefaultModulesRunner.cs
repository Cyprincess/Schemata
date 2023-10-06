using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Modular;

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
            InvokerUtilities.CallMethod(startup, nameof(ConfigureServices), services, configuration, environment);

            services.TryAddSingleton(startup.GetType(), _ => startup);
            services.TryAddEnumerableSingleton(typeof(IModule), startup.GetType());
        }
    }

    public void Configure(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment) {
        var modules = _options.GetModules();
        if (modules is not { Count: > 0 }) {
            return;
        }

        var sp = app.ApplicationServices;

        var startups = modules.Select(m => (IModule)Activator.CreateInstance(m)!).ToList();

        startups.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var startup in startups) {
            InvokerUtilities.CallMethod(sp, startup, nameof(Configure), app, configuration, environment);
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
