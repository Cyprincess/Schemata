using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Schemata.Modular;

public class ModularStartup : IStartupFilter
{
    private readonly IModulesRunner      _runner;
    private readonly IConfiguration      _configuration;
    private readonly IWebHostEnvironment _environment;

    private ModularStartup(IModulesRunner runner, IConfiguration configuration, IWebHostEnvironment environment) {
        _runner        = runner;
        _configuration = configuration;
        _environment   = environment;
    }

    public static ModularStartup Create(
        IModulesRunner      runner,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        return new ModularStartup(runner, configuration, environment);
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
        return app => {
            _runner.Configure(app, _configuration, _environment);

            next(app);
        };
    }
}
