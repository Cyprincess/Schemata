using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Advisors;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Foundation.Handlers;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Internal;
using ProcessDefinitionInfo = Schemata.Flow.Skeleton.Models.ProcessDefinitionInfo;
using ProcessSnapshot = Schemata.Flow.Skeleton.Models.ProcessSnapshot;

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
        services.TryAddScoped<InProcessRequestDispatcher>();
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());

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
        services.TryAddScoped<FlowHandlerSupport>();
        services.TryAddScoped<FlowRunner>();
        services.TryAddScoped<IFlowRunner>(sp => sp.GetRequiredService<FlowRunner>());
        services.TryAddScoped<FlowSourceLoader>();
        services.TryAddScoped<FlowStartProcessHandler>();
        services.TryAddScoped<CompleteActivityHandler>();
        services.TryAddScoped<CorrelateMessageHandler>();
        services.TryAddScoped<ThrowSignalHandler>();
        services.TryAddScoped<TerminateProcessHandler>();
        services.TryAddScoped<CancelTokenHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(IFlowSourceAdvisor<>),
            typeof(AdviceSourceProjection<>)));

        services.TryAddKeyedTransient<IRequestHandler<StartProcessRequest, SchemataProcess>, DefaultStartProcessHandler>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<StartProcessRequest, SchemataProcess>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<StartProcessRequest, SchemataProcess>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<CompleteActivityRequest, ProcessSnapshot>, DefaultCompleteActivityHandler>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<CompleteActivityRequest, ProcessSnapshot>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<CompleteActivityRequest, ProcessSnapshot>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<CorrelateMessageRequest, ProcessSnapshot>, DefaultCorrelateMessageHandler>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<CorrelateMessageRequest, ProcessSnapshot>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<CorrelateMessageRequest, ProcessSnapshot>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<RunEventRequest, ProcessSnapshot>, DefaultRunEventHandler>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<RunEventRequest, ProcessSnapshot>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<RunEventRequest, ProcessSnapshot>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<
            IRequestHandler<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>,
            DefaultThrowSignalHandler>(FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>, DefaultDeliverSignalHandler>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<TerminateProcessRequest, ProcessSnapshot>, DefaultTerminateProcessHandler>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<TerminateProcessRequest, ProcessSnapshot>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<TerminateProcessRequest, ProcessSnapshot>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<CancelTokenRequest, ProcessSnapshot>, DefaultCancelTokenHandler>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<CancelTokenRequest, ProcessSnapshot>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<CancelTokenRequest, ProcessSnapshot>>(
                FlowConstants.Handlers.Default));

        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>, ResourceMethodForwardHandler<SchemataProcess, StartProcessRequest, SchemataProcess>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<SchemataProcess, CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>, ResourceMethodForwardHandler<SchemataProcess, CompleteActivityRequest, ProcessSnapshot>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<SchemataProcess, CorrelateMessageRequest, ProcessSnapshot>, ProcessSnapshot>, ResourceMethodForwardHandler<SchemataProcess, CorrelateMessageRequest, ProcessSnapshot>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<SchemataProcess, ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>, ResourceMethodForwardHandler<SchemataProcess, ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<SchemataProcess, DeliverSignalRequest, SignalDeliveryResult>, SignalDeliveryResult>, ResourceMethodForwardHandler<SchemataProcess, DeliverSignalRequest, SignalDeliveryResult>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<SchemataProcess, TerminateProcessRequest, ProcessSnapshot>, ProcessSnapshot>, ResourceMethodForwardHandler<SchemataProcess, TerminateProcessRequest, ProcessSnapshot>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<SchemataProcessToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>, ResourceMethodForwardHandler<SchemataProcessToken, CancelTokenRequest, ProcessSnapshot>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<SchemataProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>, ResourceMethodForwardHandler<SchemataProcess, RunEventRequest, ProcessSnapshot>>();

        services.TryAddKeyedTransient<
            IRequestHandler<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>,
            DefaultListProcessDefinitionsHandler>(FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>>(
                FlowConstants.Handlers.Default));

        return services;
    }
}
