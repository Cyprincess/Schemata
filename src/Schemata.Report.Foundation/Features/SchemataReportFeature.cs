using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Common;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Insight.Foundation.Features;
using Schemata.Report.Foundation.Definitions;
using Schemata.Report.Foundation.Internal;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
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
    ) {
        ValidateResourceNames();
        EnsureSingleRegistration(services);
        RegisterInfrastructure(services);
    }

    private static void RegisterInfrastructure(IServiceCollection services) {
        services.Configure<SchemataReportOptions>(_ => { });
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ReportExecutionContext>();

        services.TryAddScoped<GenerateHandler<TReport>>();
        services.TryAddScoped<ReadSnapshotHandler<TSnapshot>>();

        services.TryAddSingleton<ReportRetentionEnforcer<TSnapshot, TChunk>>();
        services.TryAddScoped<ReportSnapshotWriter<TReport, TSnapshot, TChunk>>();
        services.TryAddScoped<IReportSnapshotStore, DefaultReportSnapshotStore<TSnapshot, TChunk>>();
        services.TryAddScoped<IReportService, DefaultReportService<TReport, TSnapshot, TChunk>>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionSource, ConfigurationReportDefinitionStore>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionSource, DatabaseReportDefinitionStore<TReport>>());
        services.TryAddSingleton<IReportDefinitionStore, CompositeReportDefinitionStore>();
        services.AddScheduledJob<ReportGenerationJob<TReport, TSnapshot, TChunk>>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IScheduledJobKeyResolver, ReportJobKeyResolver<TReport, TSnapshot, TChunk>>());
    }

    private static void EnsureSingleRegistration(IServiceCollection services) {
        var implementation = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(IReportService))
                                     ?.ImplementationType;
        if (implementation is not { IsGenericType: true }
         || implementation.GetGenericTypeDefinition() != typeof(DefaultReportService<,,>)) {
            return;
        }

        var arguments = implementation.GetGenericArguments();
        if (arguments[0] == typeof(TReport) && arguments[1] == typeof(TSnapshot) && arguments[2] == typeof(TChunk)) {
            return;
        }

        throw new InvalidOperationException(
            "Schemata Report supports only one UseReport per host. "
            + $"Existing types are {arguments[0].FullName}, {arguments[1].FullName}, and {arguments[2].FullName}."
        );
    }

    private static void ValidateResourceNames() {
        ValidateResourceName(typeof(TReport), "reports/{report}", "reports", "Report");
        ValidateResourceName(typeof(TSnapshot), "reports/{report}/snapshots/{snapshot}", "reports/{report}/snapshots", "Snapshot");
        ValidateResourceName(typeof(TChunk), "reports/{report}/snapshots/{snapshot}/chunks/{chunk}", "reports/{report}/snapshots/{snapshot}/chunks", "Chunk");
    }

    private static void ValidateResourceName(Type type, string pattern, string collectionPath, string singular) {
        var descriptor = ResourceNameDescriptor.ForType(type);
        if (descriptor.Pattern == pattern && descriptor.CollectionPath == collectionPath && descriptor.Singular == singular) {
            return;
        }

        throw new InvalidOperationException(
            $"Report entity '{type.FullName}' must re-declare [CanonicalName(\"{pattern}\")] and [DisplayName(\"{singular}\")] to preserve its report resource collection."
        );
    }
}
