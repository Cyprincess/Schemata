using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Modular;

namespace Schemata.Modular;

public class DefaultModulesRunner : IModulesRunner
{
    private readonly SchemataOptions _options;

    public DefaultModulesRunner(IOptions<SchemataOptions> options) {
        _options = options.Value;
    }

    public void ConfigureServices(
        IServiceCollection  services,
        IConfiguration      conf,
        IWebHostEnvironment env,
        IServiceProvider    provider) {
        var modules = _options.GetModules();
        if (modules is not { Count: > 0 }) {
            return;
        }

        var startups = modules.Select(m => (IModule)ActivatorUtilities.CreateInstance(provider, m)).ToList();

        startups.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var startup in startups) {
            InvokerUtilities.CallMethod(provider, startup, nameof(ConfigureServices), services, conf, env);

            services.TryAddSingleton(startup.GetType(), _ => startup);
            services.TryAddEnumerableSingleton(typeof(IModule), startup.GetType());
        }
    }

    public void Configure(IApplicationBuilder app, IConfiguration conf, IWebHostEnvironment env) {
        var modules = _options.GetModules();
        if (modules is not { Count: > 0 }) {
            return;
        }

        var sp = app.ApplicationServices;

        var startups = modules.Select(m => (IModule)ActivatorUtilities.CreateInstance(sp, m)).ToList();

        startups.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var startup in startups) {
            InvokerUtilities.CallMethod(sp, startup, nameof(Configure), app, conf, env);
        }
    }
}
