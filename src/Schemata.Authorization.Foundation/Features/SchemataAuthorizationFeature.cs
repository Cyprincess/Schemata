using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
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

[DependsOn<SchemataControllersFeature>]
[Information("Authorization depends on Controllers feature, it will be added automatically.", Level = LogLevel.Debug)]
public class SchemataAuthorizationFeature<TApplication, TAuthorization, TScope, TToken> : FeatureBase
    where TApplication : SchemataApplication
    where TAuthorization : SchemataAuthorization
    where TScope : SchemataScope
    where TToken : SchemataToken
{
    public override int Priority => 320_000_000;

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

        var part = new SchemataExtensionPart<SchemataAuthorizationFeature<TApplication, TAuthorization, TScope, TToken>>();
        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => { manager.ApplicationParts.Add(part); });

        services.AddOpenIddict()
                .AddCore(builder => {
                     builder.DisableAdditionalFiltering();

                     builder.SetDefaultApplicationEntity<TApplication>()
                            .SetDefaultAuthorizationEntity<TAuthorization>()
                            .SetDefaultScopeEntity<TScope>()
                            .SetDefaultTokenEntity<TToken>();

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
