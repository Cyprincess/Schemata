using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

/// <summary>
///     Enables cookie policy middleware using the deferred
///     <see cref="CookiePolicyOptions" /> configurator.
/// </summary>
public sealed class SchemataCookiePolicyFeature : FeatureBase
{
    /// <summary>
    ///     Priority for ordering the middleware registration in the application pipeline.
    /// </summary>
    public const int DefaultPriority = SchemataHttpsFeature.DefaultPriority + 20_000_000;

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
        var configure = configurators.PopOrDefault<CookiePolicyOptions>();
        services.AddCookiePolicy(configure);
    }

    /// <inheritdoc />
    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseCookiePolicy();
    }
}
