using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Push.Foundation;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Foundation.Handlers;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods registering the Push runtime capability.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers Push facades, dispatcher capability, and the five Foundation request handlers.</summary>
    public static IServiceCollection AddSchemataPush(this IServiceCollection services) {
        services.TryAddScoped<InProcessRequestDispatcher>();
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());

        AddHandler<SendPushRequest, ImmutableArray<TransportResult>, SendPushHandler>(services);
        AddHandler<AddPushSubscriptionRequest, PushSubscriptionResult, AddPushSubscriptionHandler>(services);
        AddHandler<RemovePushSubscriptionRequest, Unit, RemovePushSubscriptionHandler>(services);
        AddHandler<GetPushSubscriptionsQuery, IReadOnlyList<SchemataPushSubscription>, GetPushSubscriptionsHandler>(services);
        AddHandler<ExistsPushSubscriptionQuery, bool, ExistsPushSubscriptionHandler>(services);

        services.TryAddScoped<IPushService, DefaultPushService>();
        services.TryAddScoped<IPushSubscriptionManager, DefaultPushSubscriptionManager>();
        return services;
    }

    private static void AddHandler<TRequest, TResponse, THandler>(IServiceCollection services)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse> {
        services.TryAddKeyedScoped<IRequestHandler<TRequest, TResponse>, THandler>(
            PushConstants.Handlers.Default);
        services.TryAddScoped<IRequestHandler<TRequest, TResponse>>(sp =>
            sp.GetRequiredKeyedService<IRequestHandler<TRequest, TResponse>>(
                PushConstants.Handlers.Default));
    }
}
