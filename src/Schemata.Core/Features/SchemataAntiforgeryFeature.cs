using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core.State;

namespace Schemata.Core.Features;

/// <summary>
///     Enables antiforgery token validation. Registers the native antiforgery
///     services and wires the antiforgery state branch after authentication,
///     authorization.
/// </summary>
public sealed class SchemataAntiforgeryFeature : FeatureBase
{
    /// <summary>
    ///     Default middleware priority for antiforgery, after authentication,
    ///     authorization.
    /// </summary>
    public const int DefaultPriority = SchemataAuthorizationFeature.DefaultPriority + 2_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.Pop<AntiforgeryOptions>();
        services.AddAntiforgery(configure);
        services.AddStatePipelineProvider();
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseStatePipeline(StatePipeline.Antiforgery);
    }
}
