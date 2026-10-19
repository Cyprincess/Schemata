using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton;
using Schemata.Security.Skeleton;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Security.Skeleton.Advisors;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Registers the Flow resource authentication, authorization, and read-entitlement pipelines
///     closed on the host-selected process, token, transition, and participant entity types.
/// </summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
/// <typeparam name="TParticipant">The persisted participant entity type registered by the host.</typeparam>
internal static class FlowAuthorizationRegistration<TProcess, TToken, TTransition, TParticipant>
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TTransition : SchemataProcessTransition
    where TParticipant : SchemataProcessParticipant, new()
{
    internal static IServiceCollection AddFlowAuthentication(IServiceCollection services) {
        AddAuthentication<ResourceMethodRequest<TProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<TProcess, Commands.CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<TProcess, Commands.CorrelateMessageRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<TProcess, Commands.ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<TProcess, DeliverSignalRequest, SignalDeliveryResult>, SignalDeliveryResult>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<TProcess, TerminateProcessRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<TToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TToken), request.Name));
        AddAuthentication<ResourceMethodRequest<TProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        return services;
    }

    internal static IServiceCollection AddFlowAuthorization(IServiceCollection services) {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IFlowSubjectResolver, FlowSubjectResolver>();
        services.TryAddScoped<FlowAccessPolicy<TProcess, TToken, TParticipant>>();
        services.TryAddScoped<FlowParticipantManager<TProcess, TToken, TParticipant>>();
        AddReadPolicy<TProcess>(services);
        AddReadPolicy<TToken>(services);
        AddReadPolicy<TTransition>(services);
        AddAuthorization<ResourceMethodRequest<TProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<TProcess, Commands.CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<TProcess, Commands.CorrelateMessageRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<TProcess, Commands.ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<TProcess, DeliverSignalRequest, SignalDeliveryResult>, SignalDeliveryResult>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<TProcess, TerminateProcessRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<TToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TToken), request.Name));
        AddAuthorization<ResourceMethodRequest<TProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TProcess), request.Name));
        return services;
    }

    private static void AddReadPolicy<TEntity>(IServiceCollection services) where TEntity : class, ICanonicalName {
        services.AddKeyedScoped<IResourceTargetPolicy>(typeof(TEntity), (sp, _) => sp.GetRequiredService<FlowAccessPolicy<TProcess, TToken, TParticipant>>());
        services.TryAddScoped<IAccessProvider<TEntity, GetRequest>, FlowReadPolicy<TEntity, GetRequest, TProcess, TToken, TParticipant>>();
        services.TryAddScoped<IEntitlementProvider<TEntity, GetRequest>, FlowReadPolicy<TEntity, GetRequest, TProcess, TToken, TParticipant>>();
        services.TryAddScoped<IAccessProvider<TEntity, ListRequest>, FlowReadPolicy<TEntity, ListRequest, TProcess, TToken, TParticipant>>();
        services.TryAddScoped<IEntitlementProvider<TEntity, ListRequest>, FlowReadPolicy<TEntity, ListRequest, TProcess, TToken, TParticipant>>();
    }

    private static void AddAuthentication<TRequest, TResponse>(IServiceCollection services, Func<TRequest, ResourceTarget> resolve)
        where TRequest : IRequest<TResponse>, IRequestPrincipal {
        services.TryAddScoped<Func<TRequest, ResourceTarget>>(_ => resolve);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<TRequest, TResponse>), typeof(AuthenticationPipelineAdvisor<TRequest, TResponse>)));
    }

    private static void AddAuthorization<TRequest, TResponse>(IServiceCollection services, Func<TRequest, ResourceTarget> resolve)
        where TRequest : IRequest<TResponse>, IRequestPrincipal {
        services.TryAddScoped<Func<TRequest, ResourceTarget>>(_ => resolve);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<TRequest, TResponse>), typeof(AuthorizationPipelineAdvisor<TRequest, TResponse>)));
    }
}
