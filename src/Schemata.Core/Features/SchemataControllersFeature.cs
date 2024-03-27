using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

[DependsOn<SchemataRoutingFeature>]
[Information("Controllers depends on Routing feature, it will be added automatically.", Level = LogLevel.Debug)]
public class SchemataControllersFeature : FeatureBase
{
    public override int Priority => 210_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var configure = configurators.Pop<MvcOptions>();
        services.AddControllers(configure);
    }

    public override void ConfigureEndpoints(
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment) {
        endpoints.MapDefaultControllerRoute();
    }
}
