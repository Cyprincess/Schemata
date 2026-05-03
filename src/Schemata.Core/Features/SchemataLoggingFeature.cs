using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

/// <summary>
///     Configures ASP.NET Core logging via the deferred <see cref="ILoggingBuilder" />
///     configurator.
/// </summary>
public sealed class SchemataLoggingFeature : FeatureBase
{
    /// <summary>
    ///     Priority for ordering the middleware registration in the application pipeline.
    /// </summary>
    public const int DefaultPriority = SchemataExceptionHandlerFeature.DefaultPriority + 10_000_000;

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
        var configure = configurators.Pop<ILoggingBuilder>();
        services.AddLogging(configure);
    }
}
