using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Entity.Repository.Advisors;
using Schemata.Report.Foundation.Features;
using Schemata.Report.Scheduling.Advisors;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Foundation.Features;

namespace Schemata.Report.Scheduling.Features;

/// <summary>Registers periodic report scheduling and committed definition synchronization.</summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
/// <typeparam name="TSnapshot">Persisted report-snapshot entity type.</typeparam>
/// <typeparam name="TChunk">Persisted report-snapshot chunk entity type.</typeparam>
[DependsOn(typeof(SchemataReportFeature<,,>))]
[DependsOn<SchemataSchedulingFeature>]
public sealed class SchemataReportSchedulingFeature<TReport, TSnapshot, TChunk> : FeatureBase
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    /// <summary>Default feature priority for the Report.Scheduling bridge.</summary>
    public const int DefaultPriority = SchemataReportFeature<TReport, TSnapshot, TChunk>.DefaultPriority + 400_000;

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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ReportSchedulingInitializer>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryCommittedAdvisor<TReport>, AdviceReportScheduleSync<TReport>>());
    }
}
