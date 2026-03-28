using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

[DependsOn<SchemataRoutingFeature>]
public sealed class SchemataWellKnownFeature : FeatureBase
{
    public const int DefaultPriority = SchemataRoutingFeature.DefaultPriority + 5_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.PopOrDefault<WellKnownOptions>();
        var options   = new WellKnownOptions();
        configure(options);
        services.AddSingleton(options);
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        var options = app.ApplicationServices.GetRequiredService<WellKnownOptions>();

        foreach (var (suffix, handler) in options.Endpoints) {
            endpoints.MapGet($"/.well-known/{suffix}", handler)
                     .AllowAnonymous();
        }
    }
}
