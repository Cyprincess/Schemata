using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Foundation.Handlers;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;
using Schemata.Push.Skeleton.Control;
using Schemata.Push.Skeleton.Models;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods registering the Push runtime capability.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers Push facades, dispatcher capability, and the five Foundation request handlers.</summary>
    public static IServiceCollection AddSchemataPush(this IServiceCollection services) {
        services.AddInProcessRequestDispatcher();
        services.AddAuthorizationCore();

        AddHandler<SendPushRequest, ImmutableArray<TransportResult>, SendPushHandler>(services);
        AddHandler<AddPushSubscriptionRequest, SchemataPushSubscription, AddPushSubscriptionHandler>(services);
        AddHandler<RemovePushSubscriptionRequest, Unit, RemovePushSubscriptionHandler>(services);
        AddHandler<GetPushSubscriptionsQuery, IReadOnlyList<SchemataPushSubscription>, GetPushSubscriptionsHandler>(services);
        AddHandler<ExistsPushSubscriptionQuery, bool, ExistsPushSubscriptionHandler>(services);

        services.TryAddScoped<PushControlHandler>();
        services.TryAddScoped<IRequestHandler<CreatePushControlRequest, PushSubscriptionInfo>>(sp => sp.GetRequiredService<PushControlHandler>());
        services.TryAddScoped<IRequestHandler<ListPushControlRequest, IReadOnlyList<PushSubscriptionInfo>>>(sp => sp.GetRequiredService<PushControlHandler>());
        services.TryAddScoped<IRequestHandler<DeletePushControlRequest, Unit>>(sp => sp.GetRequiredService<PushControlHandler>());
        services.TryAddScoped<IRequestHandler<SendPushControlRequest, ImmutableArray<TransportResult>>>(sp => sp.GetRequiredService<PushControlHandler>());
        services.TryAddScoped<IPushOwnerResolver, DefaultPushOwnerResolver>();

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
