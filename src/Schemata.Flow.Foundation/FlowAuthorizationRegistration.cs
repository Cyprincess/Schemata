using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        AddAuthentication<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, Commands.CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, Commands.CorrelateMessageRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, Commands.ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, DeliverSignalRequest, SignalDeliveryResult>, SignalDeliveryResult>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, TerminateProcessRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthentication<ResourceMethodRequest<SchemataProcessToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => (request.Verb, typeof(SchemataProcessToken)));
        AddAuthentication<ResourceMethodRequest<SchemataProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => (request.Verb, typeof(SchemataProcess)));
        return services;
    }

    internal static IServiceCollection AddFlowAuthorization(this IServiceCollection services) {
        AddAuthorization<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, Commands.CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, Commands.CorrelateMessageRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, Commands.ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, DeliverSignalRequest, SignalDeliveryResult>, SignalDeliveryResult>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, TerminateProcessRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => (request.Verb, typeof(SchemataProcess)));
        AddAuthorization<ResourceMethodRequest<SchemataProcessToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => (request.Verb, typeof(SchemataProcessToken)));
        AddAuthorization<ResourceMethodRequest<SchemataProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>(services, static request => (request.Verb, typeof(SchemataProcess)));
        return services;
    }

    private static void AddAuthentication<TRequest, TResponse>(IServiceCollection services, Func<TRequest, (string Operation, Type? Entity)> resolve)
        where TRequest : IRequest<TResponse>, IRequestPrincipal {
        services.TryAddScoped<Func<TRequest, (string Operation, Type? Entity)>>(_ => resolve);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<TRequest, TResponse>), typeof(AuthenticationPipelineAdvisor<TRequest, TResponse>)));
    }

    private static void AddAuthorization<TRequest, TResponse>(IServiceCollection services, Func<TRequest, (string Operation, Type? Entity)> resolve)
        where TRequest : IRequest<TResponse>, IRequestPrincipal {
        services.TryAddScoped<Func<TRequest, (string Operation, Type? Entity)>>(_ => resolve);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<TRequest, TResponse>), typeof(AuthorizationPipelineAdvisor<TRequest, TResponse>)));
    }
}
