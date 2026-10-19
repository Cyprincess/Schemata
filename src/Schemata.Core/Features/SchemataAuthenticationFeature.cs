using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Core.State;

namespace Schemata.Core.Features;

/// <summary>
///     Configures authentication and authorization services and inserts authentication
///     before final tenant binding and authorization. Consumes deferred configurators for
///     <see cref="AuthenticationOptions" />,
///     <see cref="AuthenticationBuilder" />, and
///     <see cref="AuthorizationOptions" />. The public
///     <see cref="IAuthenticationService" /> is a scope-routing facade over the native
///     service, which stays resolvable per scope under
///     <see cref="ScopedAuthenticationService.HostServiceKey" />.
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
    ///     Registers the scope-routing facade and the host native service it routes to. The host
    ///     native service is constructed with
    ///     <see cref="ScopedAuthenticationHandlerProvider" /> so host handlers activate from the
    ///     host request scope; an <see cref="IAuthenticationHandlerProvider" /> registered by a
    ///     consumer is not consulted for host authentication.
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

        services.TryAddKeyedScoped<IAuthenticationService>(
            ScopedAuthenticationService.HostServiceKey,
            (sp, _) => new AuthenticationService(
                sp.GetRequiredService<IAuthenticationSchemeProvider>(),
                new ScopedAuthenticationHandlerProvider(sp, sp.GetRequiredService<IAuthenticationSchemeProvider>()),
                sp.GetRequiredService<IClaimsTransformation>(),
                sp.GetRequiredService<IOptions<AuthenticationOptions>>()));
        services.Replace(ServiceDescriptor.Scoped<IAuthenticationService, ScopedAuthenticationService>());
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseAuthentication();
    }
}
