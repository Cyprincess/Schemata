using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Insight.Foundation.Features;

/// <summary>
///     Registers the Insight query pipeline: the source catalog (built from the registered sources),
///     the plan builder, the plan executor, and the query service.
/// </summary>
public sealed class SchemataInsightFeature : FeatureBase
{
    /// <summary>The default feature priority for Insight service registration.</summary>
    public const int DefaultPriority = Orders.Extension + 110_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataInsight();
}
