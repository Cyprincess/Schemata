using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Schemata.Authorization.Foundation.Entities;
using Schemata.Authorization.Foundation.Resolver;
using Schemata.Authorization.Foundation.Stores;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Authorization.Foundation.Features;

[DependsOn<SchemataAuthenticationFeature>]
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

                     var core = builder.UseAspNetCore()
                                       .EnableStatusCodePagesIntegration();

                     if (environment.IsDevelopment()) {
                         core.DisableTransportSecurityRequirement();
                     }

                     integrate(core);

                     foreach (var feature in features) {
                         feature.ConfigureServer(services, builder);
                         feature.ConfigureServerAspNetCore(services, core);
                     }
                 })
                .AddValidation(builder => {
                     builder.UseLocalServer();
                     builder.UseAspNetCore();
                 });
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) { }
}
