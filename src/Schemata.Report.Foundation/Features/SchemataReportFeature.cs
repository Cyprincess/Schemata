using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Insight.Foundation.Features;
using Schemata.Report.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Report.Foundation.Features;

/// <summary>Registers Report options, fail-fast checks, and the transport-neutral Report services.</summary>
[DependsOn<SchemataInsightFeature>]
public sealed class SchemataReportFeature<TReport, TSnapshot, TChunk> : FeatureBase
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    /// <summary>Default feature priority for Report service registration.</summary>
    public const int DefaultPriority = Orders.Extension + 100_000_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataReport<TReport, TSnapshot, TChunk>();
}
