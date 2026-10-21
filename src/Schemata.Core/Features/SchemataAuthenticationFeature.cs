using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Schemata.Core.Features;

/// <summary>
///     Configures authentication and authorization services and inserts authentication
///     before final tenant binding and authorization. Consumes deferred configurators for
///     <see cref="AuthenticationOptions" />,
///     <see cref="AuthenticationBuilder" />, and
///     <see cref="AuthorizationOptions" />. Core registers the native ASP.NET Core
///     <see cref="IAuthenticationService" />; a component that needs per-request scheme-scope
///     routing (for example tenancy's client state profiles) installs its own facade when it
///     activates.
/// </summary>
public sealed class SchemataAuthenticationFeature : FeatureBase
{
    /// <summary>Key under which the framework's native policy evaluator is registered.</summary>
    public const string PolicyEvaluatorServiceKey = "Schemata.Authentication.PolicyEvaluator";

    /// <summary>
    ///     Default middleware priority for authentication and authorization.
    /// </summary>
    public const int DefaultPriority = SchemataCorsFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    /// <summary>
    ///     Registers the native authentication/authorization services and deferred configurators.
    ///     Scheme-scope routing is a consumer concern installed by that component's own feature.
    /// </summary>
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

        // AddAuthorization TryAdds the native evaluator, so this composition must precede it.
        services.AddSchemataPolicyEvaluator();
        var authorize = configurators.PopOrDefault<AuthorizationOptions>();
        services.AddAuthorization(authorize);

    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseAuthentication();
    }
}
