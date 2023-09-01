using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Features;

public class SchemataAuthenticationFeature : FeatureBase
{
    public override int Priority => 170_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var authenticate = configurators.Get<AuthenticationOptions>();
        var builder      = services.AddAuthentication(authenticate);

        var build = configurators.Get<AuthenticationBuilder>();
        build(builder);

        var authorize = configurators.Get<AuthorizationOptions>();
        services.AddAuthorization(authorize);
    }

    public override void Configure(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
