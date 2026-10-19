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
    ///     Registers the runtime host for the default Flow entity types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataFlow(this IServiceCollection services) {
        return services.AddSchemataFlow<
            SchemataProcess, SchemataProcessToken, SchemataProcessTransition,
            SchemataProcessCompensation, SchemataProcessParticipant, SchemataProcessSource>();
    }

    /// <summary>
    ///     Registers the process registry, persistence, runner, lifecycle notifier, source-projection
    ///     advisor, and the resource-method handlers closed on the host-selected entity types. The
    ///     registry is built once and seeded from the process configurations declared on
    ///     <c>SchemataFlowOptions</c>.
    /// </summary>
    /// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
    /// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
    /// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
    /// <typeparam name="TCompensation">The persisted compensation entity type registered by the host.</typeparam>
    /// <typeparam name="TParticipant">The persisted participant entity type registered by the host.</typeparam>
    /// <typeparam name="TSource">The persisted source-binding entity type registered by the host.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataFlow<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(
        this IServiceCollection services
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new() {
        services.AddInProcessRequestDispatcher();

        services.TryAddSingleton<IProcessRegistry>(sp => {
            var registry = ActivatorUtilities.CreateInstance<ProcessRegistry>(sp);
            var configs  = sp.GetRequiredService<IOptions<SchemataFlowOptions>>().Value.Configurations;
            foreach (var config in configs) {
                registry.Register(config);
            }

            return registry;
        });

        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(IFlowSourceAdvisor<>),
            typeof(AdviceSourceProjection<>)));

        return services.AddFlowExecution<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>();
    }

    /// <summary>
    ///     Registers the closed Flow access graph for the default entity types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFlowAccessExecution(this IServiceCollection services) {
        return services.AddFlowAccessExecution<SchemataProcess, SchemataProcessToken, SchemataProcessParticipant>();
    }

    /// <summary>
    ///     Registers the closed Flow access graph — subject resolution, participant policy, and the
    ///     participant manager — that tenant execution callbacks mirror when the host enables Flow
    ///     authorization. The graph resolves its dependencies through the composing scope.
    /// </summary>
    /// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
    /// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
    /// <typeparam name="TParticipant">The persisted participant entity type registered by the host.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFlowAccessExecution<TProcess, TToken, TParticipant>(this IServiceCollection services)
        where TProcess : SchemataProcess
        where TToken : SchemataProcessToken
        where TParticipant : SchemataProcessParticipant, new() {
        services.TryAddScoped<IFlowSubjectResolver, FlowSubjectResolver>();
        services.TryAddScoped<FlowAccessPolicy<TProcess, TToken, TParticipant>>();
        services.TryAddScoped<FlowParticipantManager<TProcess, TToken, TParticipant>>();
        return services;
    }

    /// <summary>
    ///     Registers the closed Flow execution graph for the default entity types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFlowExecution(this IServiceCollection services) {
        return services.AddFlowExecution<
            SchemataProcess, SchemataProcessToken, SchemataProcessTransition,
            SchemataProcessCompensation, SchemataProcessParticipant, SchemataProcessSource>();
    }

    /// <summary>
    ///     Registers the closed Flow execution graph — runner, persistence, handlers, and the
    ///     resource-method forwarders — without the host-owned process registry or the open-generic
    ///     source advisor. A tenant execution configure calls this entry so tenant requests resolve
    ///     the runner and handlers with their tenant-bound repositories.
    /// </summary>
    /// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
    /// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
    /// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
    /// <typeparam name="TCompensation">The persisted compensation entity type registered by the host.</typeparam>
    /// <typeparam name="TParticipant">The persisted participant entity type registered by the host.</typeparam>
    /// <typeparam name="TSource">The persisted source-binding entity type registered by the host.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFlowExecution<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(
        this IServiceCollection services
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new() {
        services.AddInProcessRequestDispatcher();

        services.TryAddSingleton<ProcessPersistence<TProcess, TToken, TTransition, TCompensation, TSource>>();
        services.TryAddScoped<ProcessLifecycleNotifier>();
        services.TryAddScoped<FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>>();
        services.TryAddScoped<FlowRunner<TProcess, TToken>>();
        services.TryAddScoped<IFlowRunner<TProcess, TToken>>(sp => sp.GetRequiredService<FlowRunner<TProcess, TToken>>());
        services.TryAddScoped<FlowSourceLoader>();
        services.TryAddScoped<FlowStartProcessHandler>();
        services.TryAddScoped<CompleteActivityHandler>();
        services.TryAddScoped<CorrelateMessageHandler>();
        services.TryAddScoped<ThrowSignalHandler>();
        services.TryAddScoped<TerminateProcessHandler>();
        services.TryAddScoped<CancelTokenHandler<TToken>>();

        services.TryAddKeyedTransient<IRequestHandler<StartProcessRequest, SchemataProcess>, DefaultStartProcessHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<StartProcessRequest, SchemataProcess>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<StartProcessRequest, SchemataProcess>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<CompleteActivityRequest, ProcessSnapshot>, DefaultCompleteActivityHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<CompleteActivityRequest, ProcessSnapshot>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<CompleteActivityRequest, ProcessSnapshot>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<CorrelateMessageRequest, ProcessSnapshot>, DefaultCorrelateMessageHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<CorrelateMessageRequest, ProcessSnapshot>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<CorrelateMessageRequest, ProcessSnapshot>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<RunEventRequest, ProcessSnapshot>, DefaultRunEventHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<RunEventRequest, ProcessSnapshot>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<RunEventRequest, ProcessSnapshot>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<
            IRequestHandler<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>,
            DefaultThrowSignalHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>>(FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>, DefaultDeliverSignalHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<TerminateProcessRequest, ProcessSnapshot>, DefaultTerminateProcessHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<TerminateProcessRequest, ProcessSnapshot>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<TerminateProcessRequest, ProcessSnapshot>>(
                FlowConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<CancelTokenRequest, ProcessSnapshot>, DefaultCancelTokenHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>>(
            FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<CancelTokenRequest, ProcessSnapshot>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<CancelTokenRequest, ProcessSnapshot>>(
                FlowConstants.Handlers.Default));

        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>, ResourceMethodForwardHandler<TProcess, StartProcessRequest, SchemataProcess>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TProcess, CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>, ResourceMethodForwardHandler<TProcess, CompleteActivityRequest, ProcessSnapshot>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TProcess, CorrelateMessageRequest, ProcessSnapshot>, ProcessSnapshot>, ResourceMethodForwardHandler<TProcess, CorrelateMessageRequest, ProcessSnapshot>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TProcess, ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>, ResourceMethodForwardHandler<TProcess, ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TProcess, DeliverSignalRequest, SignalDeliveryResult>, SignalDeliveryResult>, ResourceMethodForwardHandler<TProcess, DeliverSignalRequest, SignalDeliveryResult>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TProcess, TerminateProcessRequest, ProcessSnapshot>, ProcessSnapshot>, ResourceMethodForwardHandler<TProcess, TerminateProcessRequest, ProcessSnapshot>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>, ResourceMethodForwardHandler<TToken, CancelTokenRequest, ProcessSnapshot>>();
        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<TProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>, ResourceMethodForwardHandler<TProcess, RunEventRequest, ProcessSnapshot>>();

        services.TryAddKeyedTransient<
            IRequestHandler<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>,
            DefaultListProcessDefinitionsHandler>(FlowConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>>(
                FlowConstants.Handlers.Default));

        return services;
    }
}
