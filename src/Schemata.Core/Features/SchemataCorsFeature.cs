using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

/// <summary>
///     Enables CORS middleware using the deferred <see cref="CorsOptions" />
///     configurator.
/// </summary>
public sealed class SchemataCorsFeature : FeatureBase
{
    /// <summary>
    ///     Default middleware priority for CORS. Expressed against routing rather than rate
    /// limiting so the two can be reordered independently; CORS runs right after routing and
    /// before tenant resolution and authentication.
    /// </summary>
    public const int DefaultPriority = SchemataRoutingFeature.DefaultPriority + 20_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.Pop<CorsOptions>();
        services.AddCors(configure);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseCors();
    }
}
