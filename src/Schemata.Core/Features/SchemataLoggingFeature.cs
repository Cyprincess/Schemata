using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

/// <summary>
///     Configures the application logging infrastructure.
/// </summary>
public sealed class SchemataLoggingFeature : FeatureBase
{
    public const int DefaultPriority = SchemataExceptionHandlerFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.Pop<ILoggingBuilder>();
        services.AddLogging(configure);
    }
}
