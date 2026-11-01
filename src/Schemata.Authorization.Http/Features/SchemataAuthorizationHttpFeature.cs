using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Http.Linking;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Core.Building;
using Schemata.Resource.Http.Features;

namespace Schemata.Authorization.Http.Features;

/// <summary>
///     Exposes the host-selected authorization entities as HTTP management resources.
/// </summary>
[DependsOn(typeof(SchemataAuthorizationFeature<,,,,,>))]
[DependsOn<SchemataHttpResourceFeature>]
public sealed class SchemataAuthorizationHttpFeature : FeatureBase
{
    public const int DefaultPriority = SchemataAuthorizationOrders.Feature + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var resources = new SchemataResourceBuilder(schemata, services) {
            AuthenticationScheme = schemata.Get<string>(SchemataAuthorizationBuilder.AuthenticationSchemeKey),
        };
        var registration = schemata.Get<IAuthorizationResourceRegistration>(SchemataAuthorizationBuilder.ResourceRegistrationKey)
                        ?? throw new InvalidOperationException(
                               $"No {nameof(IAuthorizationResourceRegistration)} is registered; install the authorization feature first.");
        registration.Register(resources, [HttpResourceAttribute.Name]);

        services.AddSchemataLinkContributor<AuthorizationLinkContributor>();
    }
}
