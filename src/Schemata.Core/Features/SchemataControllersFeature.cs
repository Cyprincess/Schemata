using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

/// <summary>
///     Registers MVC controllers with endpoint routing and applies the deferred
///     <see cref="MvcOptions" /> / <see cref="IMvcBuilder" /> configurators.
/// </summary>
[DependsOn<SchemataRoutingFeature>]
public sealed class SchemataControllersFeature : FeatureBase
{
    /// <summary>Key under which the framework's native controller factory is registered.</summary>
    public const string ControllerFactoryServiceKey = "Schemata.Controllers.ControllerFactory";

    /// <summary>
    ///     Default middleware priority for MVC controller endpoints.
    /// </summary>
    public const int DefaultPriority = SchemataSessionFeature<ISessionStore>.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataControllers(
            configurators.PopOrDefault<MvcOptions>(),
            configurators.PopOrDefault<IMvcBuilder>());
        services.AddSchemataControllerFactory();
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        endpoints.MapDefaultControllerRoute();
    }
}
