using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Schemata.Core;

public class SchemataStartup : IStartupFilter
{
    private readonly IConfiguration      _configuration;
    private readonly IWebHostEnvironment _environment;

    private SchemataStartup(IConfiguration configuration, IWebHostEnvironment environment) {
        _configuration = configuration;
        _environment   = environment;
    }

    #region IStartupFilter Members

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
        return app => {
            app.UseSchemata(_configuration, _environment);

            if (app.ApplicationServices.GetService(typeof(EndpointDataSource)) is not null) {
                app.UseEndpoints(endpoints => { endpoints.UseSchemata(app, _configuration, _environment); });
            }

            next(app);
        };
    }

    #endregion

    public static SchemataStartup Create(IConfiguration configuration, IWebHostEnvironment environment) {
        return new(configuration, environment);
    }
}
