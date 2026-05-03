using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

namespace Schemata.Core.Features;

/// <summary>
///     Contract for a Schemata feature that participates in three lifecycle phases:
///     service registration, middleware pipeline, and endpoint configuration.
/// </summary>
public interface ISimpleFeature : IFeature
{
    /// <summary>
    ///     Registers services and options for this feature. Called during
    ///     <see cref="SchemataBuilder.Invoke" /> before the middleware pipeline is built.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="schemata">The central options store for cross-feature data.</param>
    /// <param name="configurators">Deferred configurator registry.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    );

    /// <summary>
    ///     Inserts middleware into the request pipeline. Called by
    ///     <see cref="SchemataStartup" /> after service registration.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment);

    /// <summary>
    ///     Maps endpoints. Only invoked when an <see cref="EndpointDataSource" />
    ///     is registered in the container.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    );
}
