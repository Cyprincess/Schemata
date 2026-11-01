using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Catalog;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Handlers;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton;
using Schemata.Insight.Skeleton.Catalog;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Insight.Skeleton.Models;
using Schemata.Messaging.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods registering the Insight federated-query runtime.</summary>
public static class SchemataInsightServiceCollectionExtensions
{
    /// <summary>Registers the source catalog, planning and execution pipeline, facade, and request handler.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataInsight(this IServiceCollection services) {
        services.AddDataProtection();

        services.AddOptions<SchemataInsightOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInsightSourceCatalog, InMemoryInsightSourceCatalog>());

        AddInsightExecution(services);

        return services;
    }

    /// <summary>
    ///     Registers the closed Insight execution graph — the in-process dispatcher, the planning
    ///     and execution pipeline, the facade, and the request handler slots — into
    ///     <paramref name="services" />. The global registration calls this entry, and a tenant
    ///     execution configure calls the same entry so tenant requests resolve these services from
    ///     the tenant boundary; the source catalog, options, and data protection stay host-owned
    ///     because they are shared collaborators, not per-tenant executions.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInsightExecution(this IServiceCollection services) {
        services.AddInProcessRequestDispatcher();

        services.TryAddSingleton<InsightPlanBuilder>();
        services.TryAddSingleton<LocalPipelineExecutor>();

        // Scoped, not singleton: PlanExecutor.OpenSourceAsync resolves IInsightSourceAdvisor from its
        // captured IServiceProvider. DefaultQueryInsightHandler is a transient resolved inside the
        // per-call request scope (see DefaultInsightService.QueryAsync), and DefaultReportService is
        // scoped too, so both constructor-inject PlanExecutor from that same live scope. A singleton
        // PlanExecutor would instead capture the root provider once at first resolution, so a scoped
        // (or request-context-reading) source advisor would either fail scope validation or silently
        // run against the wrong (root) scope.
        services.TryAddScoped<PlanExecutor>();
        services.TryAddSingleton<IInsightService, DefaultInsightService>();

        services.TryAddKeyedTransient<
            IRequestHandler<QueryInsightRequest, QueryInsightResponse>,
            DefaultQueryInsightHandler>(InsightConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<QueryInsightRequest, QueryInsightResponse>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<QueryInsightRequest, QueryInsightResponse>>(
                InsightConstants.Handlers.Default));

        return services;
    }
}
