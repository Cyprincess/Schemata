using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

/// <summary>
///     Configures authentication and authorization services and inserts
///     <c>UseAuthentication</c> / <c>UseAuthorization</c> into the middleware
///     pipeline. Consumes deferred configurators for
///     <see cref="AuthenticationOptions" />,
///     <see cref="AuthenticationBuilder" />, and
///     <see cref="AuthorizationOptions" />.
/// </summary>
public sealed class SchemataAuthenticationFeature : FeatureBase
{
    /// <summary>
    ///     Priority for ordering the middleware registration in the application pipeline.
    /// </summary>
    public const int DefaultPriority = SchemataCorsFeature.DefaultPriority + 10_000_000;

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
        var authenticate = configurators.PopOrDefault<AuthenticationOptions>();
        var builder      = services.AddAuthentication(authenticate);

        var build = configurators.PopOrDefault<AuthenticationBuilder>();
        build(builder);

        var authorize = configurators.PopOrDefault<AuthorizationOptions>();
        services.AddAuthorization(authorize);
    }

    /// <inheritdoc />
    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
