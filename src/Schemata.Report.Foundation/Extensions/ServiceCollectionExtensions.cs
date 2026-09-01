using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Definitions;
using Schemata.Report.Foundation.Internal;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods registering the transport-neutral Report services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Report options, services, definition stores and generation job. Fails the host
    ///     build when the entities lost their canonical patterns or when a second <c>UseReport</c>
    ///     would install a different entity triple.
    /// </summary>
    /// <typeparam name="TReport">Report entity type.</typeparam>
    /// <typeparam name="TSnapshot">Snapshot entity type.</typeparam>
    /// <typeparam name="TChunk">Snapshot chunk entity type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataReport<TReport, TSnapshot, TChunk>(this IServiceCollection services)
        where TReport : SchemataReport, new()
        where TSnapshot : SchemataReportSnapshot, new()
        where TChunk : SchemataReportSnapshotChunk, new() {
        ValidateResourceName(typeof(TReport), "reports/{report}", "reports", "Report");
        ValidateResourceName(typeof(TSnapshot), "reports/{report}/snapshots/{snapshot}", "reports/{report}/snapshots", "Snapshot");
        ValidateResourceName(typeof(TChunk), "reports/{report}/snapshots/{snapshot}/chunks/{chunk}", "reports/{report}/snapshots/{snapshot}/chunks", "Chunk");

        EnsureSingleRegistration<TReport, TSnapshot, TChunk>(services);

        services.TryAddScoped<InProcessRequestDispatcher>();
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());

        services.Configure<SchemataReportOptions>(_ => { });
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ReportExecutionContext>();

        services.TryAddScoped<GenerateHandler<TReport, TSnapshot, TChunk>>();
        AddHandler<RunReportRequest, ReportResult, RunReportHandler<TReport, TSnapshot, TChunk>>(services);
        AddHandler<GenerateReportRequest, Operation, GenerateHandler<TReport, TSnapshot, TChunk>>(services);

        // Facade method envelopes forward to the command handlers above. The generate envelope's
        // forwarder also serves the transport ':generate' method, whose resource pipeline is a
        // pass-through for this non-ICanonicalName wire command; TryAdd keeps exactly one envelope
        // handler per closure.
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TReport, RunReportRequest, ReportResult>, ReportResult>, ResourceMethodForwardHandler<TReport, RunReportRequest, ReportResult>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TReport, GenerateReportRequest, Operation>, Operation>, ResourceMethodForwardHandler<TReport, GenerateReportRequest, Operation>>();
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

        return services;
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

    private static void EnsureSingleRegistration<TReport, TSnapshot, TChunk>(IServiceCollection services)
        where TReport : SchemataReport, new()
        where TSnapshot : SchemataReportSnapshot, new()
        where TChunk : SchemataReportSnapshotChunk, new() {
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
