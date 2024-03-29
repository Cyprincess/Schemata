using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        var serve     = configurators.Pop<OpenIddictServerBuilder>();
        var integrate = configurators.Pop<OpenIddictServerAspNetCoreBuilder>();

        var features = new List<IAuthorizationFeature>();
        var build    = configurators.Pop<IList<IAuthorizationFeature>>();
        build(features);
        features.Sort((a, b) => a.Order.CompareTo(b.Order));

        services.AddOpenIddict()
                .AddServer(options => {
                     serve(options);

                     options.EnableDegradedMode();

                     var core = options.UseAspNetCore()
                                       .EnableStatusCodePagesIntegration();

                     integrate(core);

                     foreach (var feature in features) {
                         feature.ConfigureServer(services, options);
                         feature.ConfigureServerAspNetCore(services, core);
                     }
                 })
                .AddValidation(options => {
                     options.UseLocalServer();
                     options.UseAspNetCore();
                 });
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) { }
}
