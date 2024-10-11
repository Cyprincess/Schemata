using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

[DependsOn<SchemataRoutingFeature>]
[DependsOn<SchemataExceptionHandlerFeature>]
public sealed class SchemataControllersFeature : FeatureBase
{
    public override int Priority => 210_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var configure = configurators.PopOrDefault<MvcOptions>();
        var build     = configurators.PopOrDefault<IMvcBuilder>();

        var builder = services.AddControllers(configure);

        builder.ConfigureApplicationPartManager(manager => {
            var parts = manager.ApplicationParts.OfType<AssemblyPart>()
                               .Where(p => p.Name.StartsWith("Schemata."))
                               .ToArray();

            foreach (var part in parts) {
                manager.ApplicationParts.Remove(part);
            }
        });

        build(builder);
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment) {
        endpoints.MapDefaultControllerRoute();
    }
}
