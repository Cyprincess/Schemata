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
        var configure = configurators.Pop<OpenIddictServerBuilder>();

        services.AddOpenIddict()
                .AddServer(options => {
                     configure(options);

                     options.EnableDegradedMode();

                     options.UseAspNetCore();
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
