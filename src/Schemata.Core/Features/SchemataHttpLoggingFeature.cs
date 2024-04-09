using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

[Information("HTTP Logging can reduce the performance of an app", Level = LogLevel.Warning)]
[Information("HTTP Logging can potentially log personally identifiable information (PII).", Level = LogLevel.Warning)]
public sealed class SchemataHttpLoggingFeature : FeatureBase
{
    public override int Priority => 100_120_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var configure = configurators.Pop<HttpLoggingOptions>();
        services.AddHttpLogging(configure);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        app.UseHttpLogging();
    }
}
