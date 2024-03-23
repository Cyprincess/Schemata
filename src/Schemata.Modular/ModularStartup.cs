using System;
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
            app.UseModularControllers();

            next(app);

            app.UseModular(_runner, _configuration, _environment);

            app.UseEndpoints(endpoints => { endpoints.UseModular(app, _runner, _configuration, _environment); });
        };
    }

    #endregion

    public static ModularStartup Create(
        IConfiguration      configuration,
        IWebHostEnvironment environment,
        IServiceProvider    provider) {
        var runner = provider.GetRequiredService<IModulesRunner>();

        return new ModularStartup(runner, configuration, environment);
    }
}
