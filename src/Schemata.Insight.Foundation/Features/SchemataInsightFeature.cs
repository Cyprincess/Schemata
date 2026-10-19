using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Insight.Skeleton.Entities;
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

/// <summary>
///     Records the host-selected source entity for the Insight subsystem. The query pipeline is
///     entity-agnostic; the database catalog is closed on this type by
///     <see cref="SchemataInsightBuilder.UseDatabaseCatalog" />, and transports bind to this closed
///     feature through <c>DependsOn</c>.
/// </summary>
/// <typeparam name="TInsightSource">The persisted source entity type.</typeparam>
[DependsOn<SchemataInsightFeature>]
public sealed class SchemataInsightFeature<TInsightSource> : FeatureBase
    where TInsightSource : SchemataInsightSource, new()
{
    public override int Priority => SchemataInsightFeature.DefaultPriority;
}
