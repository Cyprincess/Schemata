using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

/// <summary>
///     Configures HTTP request/response logging middleware.
/// </summary>
[Information("HTTP Logging can reduce the performance of an app", Level = LogLevel.Warning)]
[Information("HTTP Logging can potentially log personally identifiable information (PII).", Level = LogLevel.Warning)]
public sealed class SchemataHttpLoggingFeature : FeatureBase
{
    public const int DefaultPriority = SchemataLoggingFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.Pop<HttpLoggingOptions>();
        services.AddHttpLogging(configure);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseHttpLogging();
    }
}
