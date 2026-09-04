using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Transport.Http.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Configures the Schemata Authorization server: options validation, managers,
///     authentication schemes, claim advisors, the discovery handler, the OAuth
///     model binder, and delegates to registered <see cref="IAuthorizationFlowFeature" />s.
/// </summary>
[DependsOn<SchemataAuthenticationFeature>]
[DependsOn<SchemataTransportHttpFeature>]
[DependsOn<SchemataWellKnownFeature>]
public sealed class SchemataAuthorizationFeature<TApp, TAuth, TScope> : FeatureBase
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization
    where TScope : SchemataScope
{
    public const int DefaultPriority = Orders.Extension + 60_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.PopOrDefault<SchemataAuthorizationOptions>();
        var options   = new SchemataAuthorizationOptions();
        configure(options);

        services.AddSchemataAuthorizationOptions(configure);
        services.AddSchemataAuthorizationFlows(schemata, configurators);
        services.AddSchemataApplicationPart<SchemataAuthorizationFeature<TApp, TAuth, TScope>>();
        services.AddSchemataAuthorization<TApp, TAuth, TScope>(options);
    }
}
