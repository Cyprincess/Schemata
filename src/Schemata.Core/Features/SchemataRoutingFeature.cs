using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

public class SchemataRoutingFeature : FeatureBase
{
    public override int Priority => 140_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        services.AddRouting();
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        app.UseRouting();
    }
}
