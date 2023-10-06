using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Modular;

public class ModularStartup : IStartupFilter
{
    private readonly IConfiguration      _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IModulesRunner      _runner;

    private ModularStartup(IModulesRunner runner, IConfiguration configuration, IWebHostEnvironment environment) {
        _runner        = runner;
        _configuration = configuration;
        _environment   = environment;
    }

    #region IStartupFilter Members

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
        return app => {
            _runner.Configure(app, _configuration, _environment);

            next(app);
        };
    }

    #endregion

    public static ModularStartup Create(
        SchemataOptions     options,
        IConfiguration      configuration,
        IWebHostEnvironment environment,
        IServiceProvider    provider) {
        var providers = provider.GetRequiredService<IEnumerable<IModulesProvider>>();
        var modules   = providers.SelectMany(p => p.GetModules()).ToList();
        options.SetModules(modules);

        var runner = provider.GetRequiredService<IModulesRunner>();

        return new ModularStartup(runner, configuration, environment);
    }
}
