using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Insight.Foundation.Features;
using Schemata.Transport.Http.Features;

namespace Schemata.Insight.Http.Features;

/// <summary>
///     Exposes the Insight query endpoint over HTTP by registering this assembly's
///     <see cref="InsightController" /> as an MVC application part. Error translation and JSON
///     wire-name rewrites come from <see cref="SchemataTransportHttpFeature" />.
/// </summary>
[DependsOn<SchemataInsightFeature>]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataInsightHttpFeature : FeatureBase
{
    /// <summary>The default endpoint priority for the Insight HTTP endpoint.</summary>
    public const int DefaultPriority = SchemataInsightFeature.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataApplicationPart<InsightController>();
    }
}
