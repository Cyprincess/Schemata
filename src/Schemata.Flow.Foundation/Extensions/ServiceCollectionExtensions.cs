using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Advisors;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods registering the Flow runtime host.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the process registry, persistence, runner, lifecycle notifier, source-projection
    ///     advisor, and the resource-method handlers. The registry is built once and seeded from the
    ///     process configurations declared on <c>SchemataFlowOptions</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataFlow(this IServiceCollection services) {
        services.TryAddSingleton<IProcessRegistry>(sp => {
            var registry = ActivatorUtilities.CreateInstance<ProcessRegistry>(sp);
            var configs  = sp.GetRequiredService<IOptions<SchemataFlowOptions>>().Value.Configurations;
            foreach (var config in configs) {
                registry.Register(config);
            }

            return registry;
        });

        services.TryAddSingleton<ProcessPersistence>();
        services.TryAddScoped<ProcessLifecycleNotifier>();
        services.TryAddScoped<FlowRunner>();
        services.TryAddScoped<IFlowRunner>(sp => sp.GetRequiredService<FlowRunner>());
        services.TryAddScoped<ProcessDefinitionQueryService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(IFlowSourceAdvisor<>),
            typeof(AdviceSourceProjection<>)));

        services.TryAddScoped<CompleteActivityHandler>();
        services.TryAddScoped<CorrelateMessageHandler>();
        services.TryAddScoped<ThrowSignalHandler>();
        services.TryAddScoped<TerminateProcessHandler>();
        services.TryAddScoped<CancelTokenHandler>();

        return services;
    }
}
