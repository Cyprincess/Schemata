using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

/// <summary>
///     Enables W3C-format request logging via the deferred
///     <see cref="W3CLoggerOptions" /> configurator. Emits warnings about
///     performance and PII at registration time.
/// </summary>
[Information("W3CLogger can reduce the performance of an app.", Level = LogLevel.Warning)]
[Information("W3CLogger can potentially log personally identifiable information (PII). Fields could contain PII aren't logged.", Level = LogLevel.Warning)]
public sealed class SchemataW3CLoggingFeature : FeatureBase
{
    public const int DefaultPriority = SchemataHttpLoggingFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.Pop<W3CLoggerOptions>();
        services.AddW3CLogging(configure);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseW3CLogging();
    }
}
