using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Resolver;
using Schemata.Authorization.Skeleton.Stores;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Authorization.Foundation.Features;

[DependsOn<SchemataAuthenticationFeature>]
[DependsOn<SchemataControllersFeature>]
[Information("Authorization depends on Authentication and Controllers feature, it will be added automatically.", Level = LogLevel.Debug)]
public class SchemataAuthorizationFeature : FeatureBase
{
    public override int Priority => 310_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var store     = configurators.Pop<OpenIddictCoreBuilder>();
        var serve     = configurators.Pop<OpenIddictServerBuilder>();
        var integrate = configurators.Pop<OpenIddictServerAspNetCoreBuilder>();

        var features = new List<IAuthorizationFeature>();
        var build    = configurators.Pop<IList<IAuthorizationFeature>>();
        build(features);
        features.Sort((a, b) => a.Order.CompareTo(b.Order));

        var part = new AssemblyPart(typeof(SchemataAuthorizationFeature).Assembly);
        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => { manager.ApplicationParts.Add(part); });

        services.AddOpenIddict()
                .AddCore(builder => {
                     builder.DisableAdditionalFiltering();

                     builder.SetDefaultApplicationEntity<SchemataApplication>()
                            .SetDefaultAuthorizationEntity<SchemataAuthorization>()
                            .SetDefaultScopeEntity<SchemataScope>()
                            .SetDefaultTokenEntity<SchemataToken>();

                     store(builder);

                     builder.ReplaceApplicationStoreResolver<SchemataApplicationStoreResolver>()
                            .ReplaceAuthorizationStoreResolver<SchemataAuthorizationStoreResolver>()
                            .ReplaceScopeStoreResolver<SchemataScopeStoreResolver>()
                            .ReplaceTokenStoreResolver<SchemataTokenStoreResolver>();

                     builder.Services.TryAddScoped(typeof(SchemataApplicationStore<,,>));
                     builder.Services.TryAddScoped(typeof(SchemataAuthorizationStore<,,>));
                     builder.Services.TryAddScoped(typeof(SchemataScopeStore<>));
                     builder.Services.TryAddScoped(typeof(SchemataTokenStore<>));
                 })
                .AddServer(builder => {
                     serve(builder);

                     var integration = builder.UseAspNetCore()
                                              .EnableStatusCodePagesIntegration();

                     if (environment.IsDevelopment()) {
                         integration.DisableTransportSecurityRequirement();
                     }

                     integrate(integration);

                     foreach (var feature in features) {
                         feature.ConfigureServer(features, services, builder);
                         feature.ConfigureServerAspNetCore(features, services, builder, integration);
                     }
                 })
                .AddValidation(builder => {
                     builder.UseLocalServer();
                     builder.UseAspNetCore();
                 });
    }
}
