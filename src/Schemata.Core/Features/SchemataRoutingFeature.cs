using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

/// <summary>
///     Registers routing services and inserts
///     <c>app.UseRouting()</c> into the middleware pipeline.
/// </summary>
public sealed class SchemataRoutingFeature : FeatureBase
{
    /// <summary>
    ///     Priority for ordering the middleware registration in the application pipeline.
    /// </summary>
    public const int DefaultPriority = SchemataCookiePolicyFeature.DefaultPriority + 10_000_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddRouting();
    }

    /// <inheritdoc />
    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseRouting();
    }
}
