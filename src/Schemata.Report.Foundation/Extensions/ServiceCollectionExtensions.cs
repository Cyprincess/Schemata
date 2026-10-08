using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Advisors;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Definitions;
using Schemata.Report.Foundation.Handlers;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Foundation.Runtime;
using Schemata.Report.Foundation.Snapshots;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Models;
using Schemata.Scheduling.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods registering the transport-neutral Report services.
/// </summary>
public static class ServiceCollectionExtensions
{
    internal const string SelectionKey = "Schemata.Report.Selection";

    /// <summary>
    ///     Registers the Report options, services, definition stores and generation job. Fails the host
    ///     build when the entities lost their canonical patterns; a later conflicting triple is guarded
    ///     off instead of installing a second set of closures.
    /// </summary>
    /// <typeparam name="TReport">Report entity type.</typeparam>
    /// <typeparam name="TSnapshot">Snapshot entity type.</typeparam>
    /// <typeparam name="TChunk">Snapshot chunk entity type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="schemata">The Schemata options bag recording the selected entity triple.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataReport<TReport, TSnapshot, TChunk>(
        this IServiceCollection services,
        SchemataOptions         schemata)
        where TReport : SchemataReport, new()
        where TSnapshot : SchemataReportSnapshot, new()
        where TChunk : SchemataReportSnapshotChunk, new() {
        ValidateResourceName(typeof(TReport), "reports/{report}", "reports", "Report");
        ValidateResourceName(typeof(TSnapshot), "reports/{report}/snapshots/{snapshot}", "reports/{report}/snapshots", "Snapshot");
        ValidateResourceName(typeof(TChunk), "reports/{report}/snapshots/{snapshot}/chunks/{chunk}", "reports/{report}/snapshots/{snapshot}/chunks", "Chunk");

        AddCapabilityGuards<TReport, TSnapshot>(services);


        var registration = schemata.Get<ReportRegistration>(SelectionKey);
        if (registration is not null) {
            schemata.Set(SelectionKey, registration.Select(typeof(TReport), typeof(TSnapshot), typeof(TChunk)));
            return services;
        }

        registration = new(typeof(TReport), typeof(TSnapshot), typeof(TChunk));
        schemata.Set(SelectionKey, registration);
        services.TryAddSingleton(_ => schemata.Get<ReportRegistration>(SelectionKey)!);
        services.AddInProcessRequestDispatcher();
        services.AddDataProtection();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRequestPipelineAdvisor<RunReportRequest, ReportResult>, ReportCommandPipelineAdvisor<TReport, RunReportRequest, ReportResult>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRequestPipelineAdvisor<GenerateReportRequest, Operation>, ReportCommandPipelineAdvisor<TReport, GenerateReportRequest, Operation>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRequestPipelineAdvisor<ReadSnapshotRequest, ReadSnapshotResponse>, ReportCommandPipelineAdvisor<TSnapshot, ReadSnapshotRequest, ReadSnapshotResponse>>());

        services.Configure<SchemataReportOptions>(_ => { });
        services.TryAddScoped<ReportExecutionContext>();

        services.TryAddScoped<GenerateHandler<TReport, TSnapshot, TChunk>>();
        AddHandler<RunReportRequest, ReportResult, RunReportHandler<TReport, TSnapshot, TChunk>>(services);
        AddHandler<GenerateReportRequest, Operation, GenerateHandler<TReport, TSnapshot, TChunk>>(services);

        // Method envelopes preserve verb policy and principal forwarding; the command pipeline
        // owns the guard before the lazy handler is resolved.
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TReport, RunReportRequest, ReportResult>, ReportResult>, ResourceMethodForwardHandler<TReport, RunReportRequest, ReportResult>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TReport, GenerateReportRequest, Operation>, Operation>, ResourceMethodForwardHandler<TReport, GenerateReportRequest, Operation>>();
        services.TryAddScoped<ReadSnapshotHandler<TSnapshot>>();
        AddHandler<ReadSnapshotRequest, ReadSnapshotResponse, ReadSnapshotHandler<TSnapshot>>(services);

        services.TryAddKeyedScoped<IReportSnapshotStore, DefaultReportSnapshotStore<TSnapshot, TChunk>>(ReportConstants.Services.Default);
        services.TryAddScoped<IReportSnapshotStore, ReportSnapshotStoreFacade<TSnapshot>>();
        services.TryAddKeyedScoped<IReportService, DefaultReportService<TReport, TSnapshot, TChunk>>(ReportConstants.Services.Default);
        services.TryAddScoped<IReportService, ReportServiceFacade<TReport, TSnapshot, TChunk>>();

        services.TryAddSingleton<ReportRetentionEnforcer<TSnapshot, TChunk>>();
        services.TryAddScoped<ReportSnapshotWriter<TReport, TSnapshot, TChunk>>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionSource, ConfigurationReportDefinitionStore>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionSource, DatabaseReportDefinitionStore<TReport>>());
        services.TryAddSingleton<IReportDefinitionStore, CompositeReportDefinitionStore>();
        services.AddScheduledJob<ReportGenerationJob<TReport, TSnapshot, TChunk>>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IScheduledJobKeyResolver, ReportJobKeyResolver<TReport, TSnapshot, TChunk>>());

        return services;
    }

    // Guards apply to every encountered triple — including a conflicting later one whose
    // business closures are skipped — so its resource pipeline also rejects before repository
    // I/O. TryAddEnumerable keeps repeated same-triple calls single.
    private static void AddCapabilityGuards<TReport, TSnapshot>(IServiceCollection services)
        where TReport : SchemataReport
        where TSnapshot : SchemataReportSnapshot {
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<TReport, TReport>), typeof(ReportEntityCrudRequestAdvisor<TReport, TReport>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<TReport, TReport>), typeof(ReportEntityCrudRequestAdvisor<TReport, TReport>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceGetRequestAdvisor<TReport>), typeof(ReportEntityRequestAdvisor<TReport>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListRequestAdvisor<TReport>), typeof(ReportEntityRequestAdvisor<TReport>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteRequestAdvisor<TReport>), typeof(ReportEntityRequestAdvisor<TReport>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<TSnapshot, TSnapshot>), typeof(ReportEntityCrudRequestAdvisor<TSnapshot, TSnapshot>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<TSnapshot, TSnapshot>), typeof(ReportEntityCrudRequestAdvisor<TSnapshot, TSnapshot>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceGetRequestAdvisor<TSnapshot>), typeof(ReportEntityRequestAdvisor<TSnapshot>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListRequestAdvisor<TSnapshot>), typeof(ReportEntityRequestAdvisor<TSnapshot>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteRequestAdvisor<TSnapshot>), typeof(ReportEntityRequestAdvisor<TSnapshot>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<TReport, RunReportRequest>), typeof(ReportEntityMethodRequestAdvisor<TReport, RunReportRequest>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<TReport, GenerateReportRequest>), typeof(ReportEntityMethodRequestAdvisor<TReport, GenerateReportRequest>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<TSnapshot, ReadSnapshotRequest>), typeof(ReportEntityMethodRequestAdvisor<TSnapshot, ReadSnapshotRequest>)));
    }


    private static void AddHandler<TRequest, TResponse, THandler>(IServiceCollection services)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse> {
        services.TryAddKeyedScoped<IRequestHandler<TRequest, TResponse>, THandler>(
            ReportConstants.Handlers.Default);
        services.TryAddScoped<IRequestHandler<TRequest, TResponse>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<TRequest, TResponse>>(
                ReportConstants.Handlers.Default));
    }

    private static void ValidateResourceName(Type type, string pattern, string collectionPath, string singular) {
        var descriptor = ResourceNameDescriptor.ForType(type);
        if (descriptor.Pattern == pattern && descriptor.CollectionPath == collectionPath && descriptor.Singular == singular) {
            return;
        }

        throw new InvalidOperationException(
            $"Report entity '{type.FullName}' must re-declare [CanonicalName(\"{pattern}\")] to preserve its report resource collection and the '{singular}' resource identity."
        );
    }
}
