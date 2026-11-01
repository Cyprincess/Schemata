using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Foundation.Handlers;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Foundation.Settlement;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Services;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods registering the Scheduling runtime host.</summary>
public static class SchemataSchedulingServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the scheduler, dispatcher, operation service, and request handlers for the
    ///     default persisted entities <see cref="SchemataJob" /> and <see cref="SchemataJobExecution" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataScheduling(this IServiceCollection services) {
        return services.AddSchemataScheduling<SchemataJob, SchemataJobExecution>();
    }

    /// <summary>
    ///     Registers the scheduler, dispatcher, operation service, and request handlers closed on
    ///     <typeparamref name="TJob" /> and <typeparamref name="TExecution" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataScheduling<TJob, TExecution>(this IServiceCollection services)
        where TJob : SchemataJob, new()
        where TExecution : SchemataJobExecution, new() {
        services.TryAddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>();
        services.AddInProcessRequestDispatcher();

        services.AddSchedulingOptions();
        services.TryAddSingleton<IScheduledJobRegistry, DefaultScheduledJobRegistry>();
        services.TryAddSingleton<IActiveExecutionRegistry, ActiveExecutionRegistry>();
        services.TryAddSingleton<JobExecutionDispatcher<TJob, TExecution>>();
        services.TryAddSingleton<DefaultScheduler<TJob, TExecution>>();
        services.TryAddSingleton<IScheduler<TJob, TExecution>>(provider => provider.GetRequiredService<DefaultScheduler<TJob, TExecution>>());
        services.TryAddSingleton<IScheduler>(provider => provider.GetRequiredService<IScheduler<TJob, TExecution>>());
        services.TryAddSingleton<IOperationService, DefaultOperationService<TJob, TExecution>>();
        services.TryAddSingleton<IJobExecutionState, JobExecutionState<TExecution>>();
        services.TryAddSingleton<SchemataJobWriteGate>();
        services.TryAddTransient<SchedulingHandlerSupport<TJob, TExecution>>();
        services.TryAddTransient<DefaultScheduleJobHandler<TJob, TExecution>>();

        services.TryAddKeyedTransient<IRequestHandler<ScheduleJobRequest<TJob>, Unit>>(
            SchedulingConstants.Handlers.Default, (provider, _) => provider.GetRequiredService<DefaultScheduleJobHandler<TJob, TExecution>>());
        services.TryAddTransient<IRequestHandler<ScheduleJobRequest<TJob>, Unit>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<ScheduleJobRequest<TJob>, Unit>>(
                SchedulingConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<UnscheduleJobRequest, Unit>, DefaultUnscheduleJobHandler<TJob, TExecution>>(
            SchedulingConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<UnscheduleJobRequest, Unit>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<UnscheduleJobRequest, Unit>>(
                SchedulingConstants.Handlers.Default));

        services.TryAddKeyedTransient<
            IRequestHandler<TriggerJobRequest<TExecution>, TExecution>,
            DefaultTriggerJobHandler<TJob, TExecution>>(SchedulingConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<TriggerJobRequest<TExecution>, TExecution>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<TriggerJobRequest<TExecution>, TExecution>>(
                SchedulingConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<RescheduleJobRequest<TJob>, Unit>, DefaultRescheduleJobHandler<TJob, TExecution>>(
            SchedulingConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<RescheduleJobRequest<TJob>, Unit>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<RescheduleJobRequest<TJob>, Unit>>(
                SchedulingConstants.Handlers.Default));

        services.TryAddTransient<IJobSettlement<TJob, TExecution>, JobSettlement<TJob, TExecution>>();

        services.AddHostedService<SchedulingInitializer<TJob, TExecution>>();
        services.AddHostedService(provider => provider.GetRequiredService<JobExecutionDispatcher<TJob, TExecution>>());
        return services;
    }

    /// <summary>
    ///     Declares the closed options services for <see cref="SchemataSchedulingOptions" />.
    ///     Tenant override collections reject open-generic descriptors, so the scheduling
    ///     registration must remain usable inside a tenant collection.
    /// </summary>
    private static void AddSchedulingOptions(this IServiceCollection services) {
        services.TryAddSingleton<IOptions<SchemataSchedulingOptions>, OptionsManager<SchemataSchedulingOptions>>();
        services.TryAddScoped<IOptionsSnapshot<SchemataSchedulingOptions>, OptionsManager<SchemataSchedulingOptions>>();
        services.TryAddSingleton<IOptionsMonitor<SchemataSchedulingOptions>, OptionsMonitor<SchemataSchedulingOptions>>();
        services.TryAddTransient<IOptionsFactory<SchemataSchedulingOptions>, OptionsFactory<SchemataSchedulingOptions>>();
        services.TryAddSingleton<IOptionsMonitorCache<SchemataSchedulingOptions>, OptionsCache<SchemataSchedulingOptions>>();
    }
}
