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

internal static class FlowAuthorizationRegistration
{
    internal static IServiceCollection AddFlowAuthentication(this IServiceCollection services) {
        AddAuthentication<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, Commands.CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, Commands.CorrelateMessageRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, Commands.ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, DeliverSignalRequest, SignalDeliveryResult>, SignalDeliveryResult>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, TerminateProcessRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthentication<ResourceMethodRequest<SchemataProcessToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcessToken), request.Name));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        return services;
    }

    internal static IServiceCollection AddFlowAuthorization(this IServiceCollection services) {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IFlowSubjectResolver, FlowSubjectResolver>();
        services.TryAddScoped<FlowAccessPolicy>();
        services.TryAddScoped<FlowParticipantManager>();
        AddReadPolicy<SchemataProcess>(services);
        AddReadPolicy<SchemataProcessToken>(services);
        AddReadPolicy<SchemataProcessTransition>(services);
        AddAuthorization<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, Commands.CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, Commands.CorrelateMessageRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, Commands.ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, DeliverSignalRequest, SignalDeliveryResult>, SignalDeliveryResult>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, TerminateProcessRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        AddAuthorization<ResourceMethodRequest<SchemataProcessToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcessToken), request.Name));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataProcess), request.Name));
        return services;
    }

    private static void AddReadPolicy<TEntity>(IServiceCollection services) where TEntity : class, ICanonicalName {
        services.AddKeyedScoped<IResourceTargetPolicy>(typeof(TEntity), (sp, _) => sp.GetRequiredService<FlowAccessPolicy>());
        services.TryAddScoped<IAccessProvider<TEntity, GetRequest>, FlowReadPolicy<TEntity, GetRequest>>();
        services.TryAddScoped<IEntitlementProvider<TEntity, GetRequest>, FlowReadPolicy<TEntity, GetRequest>>();
        services.TryAddScoped<IAccessProvider<TEntity, ListRequest>, FlowReadPolicy<TEntity, ListRequest>>();
        services.TryAddScoped<IEntitlementProvider<TEntity, ListRequest>, FlowReadPolicy<TEntity, ListRequest>>();
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
