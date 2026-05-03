using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

/// <summary>
///     Enables ASP.NET Core HTTP request/response logging via the deferred
///     <see cref="HttpLoggingOptions" /> configurator. Emits warnings at registration
///     time about performance and PII concerns.
/// </summary>
[Information("HTTP Logging can reduce the performance of an app", Level = LogLevel.Warning)]
[Information("HTTP Logging can potentially log personally identifiable information (PII).", Level = LogLevel.Warning)]
public sealed class SchemataHttpLoggingFeature : FeatureBase
{
    /// <summary>
    ///     Priority for ordering the middleware registration in the application pipeline.
    /// </summary>
    public const int DefaultPriority = SchemataLoggingFeature.DefaultPriority + 10_000_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseHttpLogging();
    }
}
