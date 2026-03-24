using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Schemata.Core;

/// <summary>
///     Startup filter that wires the Schemata middleware pipeline and endpoint configuration.
/// </summary>
public sealed class SchemataStartup : IStartupFilter
{
    private readonly IConfiguration      _configuration;
    private readonly IWebHostEnvironment _environment;

    private SchemataStartup(IConfiguration configuration, IWebHostEnvironment environment) {
        _configuration = configuration;
        _environment   = environment;
    }

    #region IStartupFilter Members

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
        return app => {
            app.UseSchemata(_configuration, _environment);

            if (app.ApplicationServices.GetService(typeof(EndpointDataSource)) is not null) {
                app.UseEndpoints(endpoints => { endpoints.UseSchemata(app, _configuration, _environment); });
            }

            app.CleanSchemata();

            next(app);
        };
    }

    #endregion

    /// <summary>
    ///     Creates a new instance of <see cref="SchemataStartup" />.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment.</param>
    /// <returns>A new <see cref="SchemataStartup" /> instance.</returns>
    public static SchemataStartup Create(IConfiguration configuration, IWebHostEnvironment environment) {
        return new(configuration, environment);
    }
}
