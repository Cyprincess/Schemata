using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Features;
using Schemata.Report.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Features;

namespace Schemata.Report.Grpc.Features;

/// <summary>Composes Report resource methods with the gRPC resource transport.</summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
/// <typeparam name="TSnapshot">Persisted report-snapshot entity type.</typeparam>
/// <typeparam name="TChunk">Persisted report-snapshot chunk entity type.</typeparam>
[DependsOn(typeof(SchemataReportFeature<,,>))]
[DependsOn<SchemataGrpcResourceFeature>]
public sealed class SchemataReportGrpcFeature<TReport, TSnapshot, TChunk> : FeatureBase
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for Report gRPC endpoints.</summary>
    public const int DefaultPriority = SchemataReportFeature<TReport, TSnapshot, TChunk>.DefaultPriority + 200_000;

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
        var resources = new SchemataResourceBuilder(schemata, services);
        resources.Use<TReport, TReport, TReport, TReport>(
            [GrpcResourceAttribute.Name],
            resource => resource.Methods = ReportResourceRegistration<TReport, TSnapshot>.ReportMethods);
        resources.Use<TSnapshot, TSnapshot, TSnapshot, TSnapshot>(
            [GrpcResourceAttribute.Name],
            resource => {
                resource.Operations = ReportResourceRegistration<TReport, TSnapshot>.SnapshotOperations;
                resource.Methods    = ReportResourceRegistration<TReport, TSnapshot>.SnapshotMethods;
            });
    }
}
