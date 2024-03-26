using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

[DependsOn<SchemataCookiePolicyFeature>]
[Information("Session may requires Cookie Policy middleware, it will be added automatically.", Level = LogLevel.Debug)]
public class SchemataSessionFeature : FeatureBase
{
    public override int Priority => 170_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var configure = configurators.Pop<SessionOptions>();
        services.AddSession(configure);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        app.UseSession();
    }
}
