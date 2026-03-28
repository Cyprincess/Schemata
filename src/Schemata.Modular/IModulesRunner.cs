using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Modular;

/// <summary>
///     Orchestrates module lifecycle phases: service registration, application configuration, and endpoint configuration.
/// </summary>
public interface IModulesRunner
{
    /// <summary>
    ///     Invokes <c>ConfigureServices</c> on each discovered module, registering their services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The hosting environment.</param>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment);

    /// <summary>
    ///     Invokes <c>ConfigureApplication</c> on each discovered module, configuring the middleware pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The hosting environment.</param>
    void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment);

    /// <summary>
    ///     Invokes <c>ConfigureEndpoints</c> on each discovered module, registering route endpoints.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The hosting environment.</param>
    void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    );
}
