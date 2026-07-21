using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Features;
using Schemata.Report.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Http.Features;

namespace Schemata.Report.Http.Features;

/// <summary>Composes Report resource methods with the HTTP resource transport.</summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
/// <typeparam name="TSnapshot">Persisted report-snapshot entity type.</typeparam>
/// <typeparam name="TChunk">Persisted report-snapshot chunk entity type.</typeparam>
[DependsOn(typeof(SchemataReportFeature<,,>))]
[DependsOn<SchemataHttpResourceFeature>]
public sealed class SchemataReportHttpFeature<TReport, TSnapshot, TChunk> : FeatureBase
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for Report HTTP endpoints.</summary>
    public const int DefaultPriority = SchemataReportFeature<TReport, TSnapshot, TChunk>.DefaultPriority + 100_000;

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
        services.AddOptions<MvcOptions>()
                .Configure(mvc => mvc.ModelMetadataDetailsProviders.Add(new ReadSnapshotBindingMetadataProvider()));

        var resources = new SchemataResourceBuilder(schemata, services);
        resources.Use<TReport, TReport, TReport, TReport>(
            [HttpResourceAttribute.Name],
            resource => resource.Methods = ReportResourceRegistration<TReport, TSnapshot>.ReportMethods);
        resources.Use<TSnapshot, TSnapshot, TSnapshot, TSnapshot>(
            [HttpResourceAttribute.Name],
            resource => {
                resource.Operations = ReportResourceRegistration<TReport, TSnapshot>.SnapshotOperations;
                resource.Methods    = ReportResourceRegistration<TReport, TSnapshot>.SnapshotMethods;
            });
    }
}
