using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Modular;
using Schemata.Core;

namespace Schemata.Modular;

public sealed class DefaultModulesRunner : IModulesRunner
{
    private readonly ILogger<DefaultModulesRunner> _logger;
    private readonly SchemataOptions               _schemata;

    public DefaultModulesRunner(SchemataOptions schemata, ILogger<DefaultModulesRunner> logger) {
        _logger   = logger;
        _schemata = schemata;
    }

    #region IModulesRunner Members

    public void ConfigureServices(
        IServiceCollection  services,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var modules = _schemata.GetModules();
        if (modules is not { Count: > 0 }) {
            return;
        }

        var startups = modules
                      .Select(m => Utilities.CreateInstance<IModule>(m.EntryType, _schemata.CreateLogger(m.EntryType))!)
                      .ToList();

        startups.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var startup in startups) {
            services.TryAddSingleton(startup.GetType(), _ => startup);
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IModule), startup));

            if (startup.GetType()
                       .GetMethod(nameof(ConfigureServices), BindingFlags.Instance | BindingFlags.Public) is null) {
                continue;
            }

            Utilities.CallMethod(startup, nameof(ConfigureServices), services, configuration, environment);
        }
    }

    public void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var sp = app.ApplicationServices;

        var startups = sp.GetServices<IModule>().ToList();

        startups.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var startup in startups) {
            if (startup.GetType()
                       .GetMethod(nameof(ConfigureApplication), BindingFlags.Instance | BindingFlags.Public) is null) {
                continue;
            }

            Utilities.CallMethod(sp, startup, nameof(ConfigureApplication), app);
        }
    }

    public void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment) {
        var sp = app.ApplicationServices;

        var startups = sp.GetServices<IModule>().ToList();

        startups.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var startup in startups) {
            if (startup.GetType()
                       .GetMethod(nameof(ConfigureEndpoints), BindingFlags.Instance | BindingFlags.Public) is null) {
                continue;
            }

            Utilities.CallMethod(sp, startup, nameof(ConfigureEndpoints), endpoints, app);
        }
    }

    #endregion
}
