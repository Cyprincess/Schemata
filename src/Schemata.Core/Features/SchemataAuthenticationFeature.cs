using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Features;

public sealed class SchemataAuthenticationFeature : FeatureBase
{
    public override int Priority => 160_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var authenticate = configurators.PopOrDefault<AuthenticationOptions>();
        var builder      = services.AddAuthentication(authenticate);

        var build = configurators.PopOrDefault<AuthenticationBuilder>();
        build(builder);

        var authorize = configurators.PopOrDefault<AuthorizationOptions>();
        services.AddAuthorization(authorize);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
