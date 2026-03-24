using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

namespace Schemata.Core.Features;

/// <summary>
///     A framework feature that participates in service registration, middleware pipeline, and endpoint configuration.
/// </summary>
public interface ISimpleFeature : IFeature
{
    /// <summary>
    ///     Configures services for this feature during startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="schemata">The Schemata options container.</param>
    /// <param name="configurators">The configurator registry.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment.</param>
    void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    );

    /// <summary>
    ///     Configures the middleware pipeline for this feature.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment.</param>
    void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment);

    /// <summary>
    ///     Configures endpoints for this feature.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment.</param>
    void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    );
}
