using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Schemata.Core;

/// <summary>
///     An <see cref="IStartupFilter" /> that injects the Schemata middleware
///     pipeline and endpoint configuration into the ASP.NET Core request pipeline
///     before the next filter in the chain.
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
    ///     Factory method that creates the filter. The constructor is private to
    ///     ensure instantiation only through this path during service registration.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment.</param>
    /// <returns>A new <see cref="SchemataStartup" /> instance.</returns>
    public static SchemataStartup Create(IConfiguration configuration, IWebHostEnvironment environment) {
        return new(configuration, environment);
    }
}
