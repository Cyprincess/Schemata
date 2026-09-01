using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Foundation.Handlers;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods registering the Scheduling runtime host.</summary>
public static class SchemataSchedulingServiceCollectionExtensions
{
    /// <summary>Registers the scheduler, dispatcher, operation service, and request handlers.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataScheduling(this IServiceCollection services) {
        services.TryAddScoped<InProcessRequestDispatcher>();
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());

        services.AddOptions<SchemataSchedulingOptions>();
        services.TryAddSingleton<IScheduledJobRegistry, DefaultScheduledJobRegistry>();
        services.TryAddSingleton<ConcurrentDictionary<string, CancellationTokenSource>>();
        services.TryAddSingleton<JobExecutionDispatcher>();
        services.TryAddSingleton<DefaultScheduler>();
        services.TryAddSingleton<IScheduler>(provider => provider.GetRequiredService<DefaultScheduler>());
        services.TryAddSingleton<IOperationService, DefaultOperationService>();
        services.TryAddSingleton<SchemataJobWriteGate>();
        services.TryAddTransient<SchedulingHandlerSupport>();

        services.TryAddKeyedTransient<IRequestHandler<ScheduleJobRequest, Unit>, DefaultScheduleJobHandler>(
            SchedulingConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<ScheduleJobRequest, Unit>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<ScheduleJobRequest, Unit>>(
                SchedulingConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<UnscheduleJobRequest, Unit>, DefaultUnscheduleJobHandler>(
            SchedulingConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<UnscheduleJobRequest, Unit>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<UnscheduleJobRequest, Unit>>(
                SchedulingConstants.Handlers.Default));

        services.TryAddKeyedTransient<
            IRequestHandler<TriggerJobRequest, SchemataJobExecution>,
            DefaultTriggerJobHandler>(SchedulingConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<TriggerJobRequest, SchemataJobExecution>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<TriggerJobRequest, SchemataJobExecution>>(
                SchedulingConstants.Handlers.Default));

        services.TryAddTransient<IRequestHandler<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>, ResourceMethodForwardHandler<SchemataJob, TriggerJobRequest, SchemataJobExecution>>();

        services.TryAddKeyedTransient<IRequestHandler<RescheduleJobRequest, Unit>, DefaultRescheduleJobHandler>(
            SchedulingConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<RescheduleJobRequest, Unit>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<RescheduleJobRequest, Unit>>(
                SchedulingConstants.Handlers.Default));

        services.TryAddKeyedTransient<IRequestHandler<StageJobExecutionResultRequest, Unit>,
            DefaultStageJobExecutionResultHandler>(SchedulingConstants.Handlers.Default);
        services.TryAddTransient<IRequestHandler<StageJobExecutionResultRequest, Unit>>(provider =>
            provider.GetRequiredKeyedService<IRequestHandler<StageJobExecutionResultRequest, Unit>>(
                SchedulingConstants.Handlers.Default));

        services.AddHostedService<SchedulingInitializer>();
        services.AddHostedService(provider => provider.GetRequiredService<JobExecutionDispatcher>());
        return services;
    }
}
