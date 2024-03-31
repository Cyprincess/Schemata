using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

[Information("W3CLogger can reduce the performance of an app.", Level = LogLevel.Warning)]
[Information("W3CLogger can potentially log personally identifiable information (PII). Fields could contain PII aren't logged.", Level = LogLevel.Warning)]
public class SchemataW3CLoggingFeature : FeatureBase
{
    public override int Priority => 100_130_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var configure = configurators.Pop<W3CLoggerOptions>();
        services.AddW3CLogging(configure);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        app.UseW3CLogging();
    }
}
